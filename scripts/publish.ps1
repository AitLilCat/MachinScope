param([string]$SigningCertificateThumbprint, [string]$TimestampUrl)
. "$PSScriptRoot\common.ps1"
$output = Join-Path $script:RepositoryRoot 'artifacts\MachinScope-win-x64'
Push-Location $script:RepositoryRoot
try {
    Invoke-CoreDotnet -Arguments @('publish','src\Core.Desktop\Core.Desktop.csproj','-c','Release','-r','win-x64','--self-contained','true','-p:WindowsAppSDKSelfContained=true','-o',$output)
    foreach ($resource in @('MachinScope.pri','App.xbf','MainWindow.xbf')) { if (!(Test-Path -LiteralPath (Join-Path $output $resource))) { throw "Published XAML resource is missing: $resource" } }
    Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot 'LICENSE') -Destination $output
    Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot 'RUNTIME-TERMS.txt') -Destination $output
    Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot 'README.release.md') -Destination (Join-Path $output 'README.md')
    python (Join-Path $PSScriptRoot 'dependency-notices.py')
    if ($LASTEXITCODE -ne 0) { throw 'Notice generation failed.' }
    Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot 'THIRD-PARTY-NOTICES') -Destination $output -Recurse -Force
    if ($SigningCertificateThumbprint) {
        if (!$TimestampUrl -or !$TimestampUrl.StartsWith('https://')) { throw 'Signing requires an approved HTTPS timestamp URL.' }
        $signTool = Get-Command signtool.exe -ErrorAction Stop
        $ownFiles = @(Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -eq 'MachinScope.exe' -or $_.Name -eq 'MachinScope.dll' -or $_.Name -match '^Core\.(Domain|Application|Windows|Persistence)\.dll$' })
        foreach ($file in $ownFiles) {
            & $signTool.Source sign /sha1 $SigningCertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $file.FullName
            if ($LASTEXITCODE -ne 0) { throw "Signing failed: $($file.FullName)" }
            & $signTool.Source verify /pa /all $file.FullName
            if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $($file.FullName)" }
        }
    }
    $zip = Join-Path $script:RepositoryRoot 'artifacts\MachinScope-win-x64.zip'
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
    Write-Output "Published folder: $output"
    Write-Output "Archive: $zip"
} finally { Pop-Location }

