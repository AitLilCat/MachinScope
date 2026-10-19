param([ValidateSet('0.1.0-beta')][string]$Version = '0.1.0-beta', [string]$SigningCertificateThumbprint, [string]$TimestampUrl, [string]$ExistingPortableExe, [switch]$IncludeSourceArchive)
. "$PSScriptRoot\common.ps1"
Push-Location $script:RepositoryRoot
try {
    [xml]$props = Get-Content Directory.Build.props
    if ($props.Project.PropertyGroup.Version -ne $Version) { throw 'Requested version differs from source metadata.' }
    & "$PSScriptRoot\build.ps1"
    & "$PSScriptRoot\test.ps1"
    New-Item artifacts -ItemType Directory -Force | Out-Null
    $audit = & $script:Dotnet list CORE.slnx package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed.' }
    $audit | Set-Content artifacts/beta-vulnerabilities.json
    $auditRecord = ($audit -join "`n") | ConvertFrom-Json
    if (@($auditRecord.logs | Where-Object level -eq 'error').Count -gt 0) { throw 'Dependency audit reported errors.' }
    foreach ($project in $auditRecord.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($dependency in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($dependency.vulnerabilities) { throw "Vulnerable dependency: $($dependency.id)" }
            }
        }
    }
    Invoke-CoreDotnet -Arguments @('build','CORE.slnx','-c','Release','--no-restore','-p:RunAnalyzersDuringBuild=true','-p:EnableNETAnalyzers=true','-p:AnalysisLevel=latest','-warnaserror')
    if ($ExistingPortableExe) {
        if ($SigningCertificateThumbprint) { throw 'Preserved artifacts cannot be modified by signing; omit ExistingPortableExe to create a new signed candidate.' }
        $exe = (Resolve-Path -LiteralPath $ExistingPortableExe).Path
        $output = Join-Path $script:RepositoryRoot ('artifacts\release-verification-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $output | Out-Null
    } else {
        $output = Join-Path $script:RepositoryRoot ('artifacts\MachinScope-release-' + [Guid]::NewGuid().ToString('N'))
        & "$PSScriptRoot\publish-portable.ps1" -OutputDirectory $output
        $exe = Join-Path $output 'MachinScope.exe'
    }
    if ($SigningCertificateThumbprint) {
        if (!$TimestampUrl -or !$TimestampUrl.StartsWith('https://')) { throw 'Signing requires an approved HTTPS timestamp URL.' }
        $signTool = (Get-Command signtool.exe -ErrorAction Stop).Source
        & $signTool sign /sha1 $SigningCertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $exe
        if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }
        & $signTool verify /pa /all $exe
        if ($LASTEXITCODE -ne 0) { throw 'Signature verification failed.' }
    }
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($metadata.ProductName -ne 'MachinScope' -or $metadata.CompanyName -ne 'AitLilCat' -or $metadata.ProductVersion -notlike '0.1.0-beta*') { throw 'Executable metadata mismatch.' }
    $probe = Join-Path $output 'bootstrap.json'
    Push-Location $env:TEMP
    try {
        $process = Start-Process -FilePath $exe -ArgumentList @('--verify-build','--report', ('"' + $probe + '"')) -PassThru -WindowStyle Hidden
        if (!$process.WaitForExit(60000)) { throw 'Bootstrap probe timed out; inspect the process.' }
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $probe) -or !((Get-Content $probe -Raw | ConvertFrom-Json).verified)) { throw 'Portable bootstrap verification failed.' }
    } finally { Pop-Location }
    if (!$ExistingPortableExe) {
        python "$PSScriptRoot\dependency-notices.py"
        if ($LASTEXITCODE -ne 0) { throw 'Notice generation failed.' }
    }
    python "$PSScriptRoot\audit-source.py"
    if ($LASTEXITCODE -ne 0) { throw 'Source-distribution audit failed.' }
    python "$PSScriptRoot\check-repository.py"
    if ($LASTEXITCODE -ne 0) { throw 'Repository consistency check failed.' }
    $packageArguments = @("$PSScriptRoot\package-delivery.py", '--portable-exe', $exe)
    if ($ExistingPortableExe) { $packageArguments += '--preserve-portable-archive' }
    if ($IncludeSourceArchive) { $packageArguments += '--include-source' }
    python @packageArguments
    if ($LASTEXITCODE -ne 0) { throw 'Package verification failed.' }
    Write-Output "Validated executable: $exe"
    Write-Output "Signing: $((Get-AuthenticodeSignature -LiteralPath $exe).Status)"
} finally { Pop-Location }
