param([ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('Auto','Exclude','Require')][string]$StandardUserProcessLaunch = 'Auto')
. "$PSScriptRoot\common.ps1"
$testOutput = Join-Path $script:RepositoryRoot ('artifacts\tests\' + [Guid]::NewGuid().ToString('N'))
Push-Location $script:RepositoryRoot
try {
    # Query the native provider in a process inheriting this host's real token.
    # Production launch policy is unchanged; elevated runners never impersonate Medium.
    $integrationProject = 'tests/Core.IntegrationTests/Core.IntegrationTests.csproj'
    Invoke-CoreDotnet -Arguments @('build',$integrationProject,'-c',$Configuration)
    $integrationAssembly = Join-Path $script:RepositoryRoot "tests/Core.IntegrationTests/bin/$Configuration/net10.0-windows10.0.19041.0/Core.IntegrationTests.dll"
    $contextJson = & $script:Dotnet $integrationAssembly --test-context
    if ($LASTEXITCODE -ne 0) { throw 'Native test-context probe failed.' }
    $context = ($contextJson -join "`n") | ConvertFrom-Json
    if ($context.integrity -notin @('Untrusted','Low','Medium','High','System')) { throw 'Cannot determine native process integrity; suite selection refused.' }
    $runStandardUser = $StandardUserProcessLaunch -ne 'Exclude' -and $context.canRunStandardUserProcessLaunch -eq $true
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $selectionRecord = [ordered]@{
        integrity = $context.integrity
        selection = $StandardUserProcessLaunch
        standardUserCategorySelected = $runStandardUser
        standardUserCategoryExecuted = $false
        excludedCategory = $(if (!$runStandardUser) { 'StandardUserProcessLaunch' } else { $null })
        exclusionReason = $(if ($StandardUserProcessLaunch -eq 'Exclude') { 'Explicit baseline-only selection; managed restart was not executed.' } elseif (!$runStandardUser) { 'A real Medium-integrity test host is required; managed restart was not executed.' } else { $null })
    }
    $contextReport = Join-Path $testOutput 'test-context.json'
    $selectionRecord | ConvertTo-Json | Set-Content -LiteralPath $contextReport -Encoding utf8
    Write-Output "Native test-host integrity: $($context.integrity); StandardUserProcessLaunch selection: $StandardUserProcessLaunch"
    if ($StandardUserProcessLaunch -eq 'Require' -and !$runStandardUser) { throw 'StandardUserProcessLaunch was required but the host is not Medium integrity.' }
    if (!$runStandardUser) { Write-Warning 'StandardUserProcessLaunch is excluded and NOT counted as passing. ProcessLaunchSecurity and terminated-process inspection still execute.' }
    Invoke-CoreDotnet -Arguments @('test','CORE.slnx','-c',$Configuration,'--filter','TestCategory!=StandardUserProcessLaunch','--logger','trx','--results-directory',$testOutput)
    if ($runStandardUser) {
        Invoke-CoreDotnet -Arguments @('test',$integrationProject,'-c',$Configuration,'--no-build','--filter','TestCategory=StandardUserProcessLaunch','--logger','trx','--results-directory',$testOutput)
        $selectionRecord.standardUserCategoryExecuted = $true
        $selectionRecord | ConvertTo-Json | Set-Content -LiteralPath $contextReport -Encoding utf8
    }
    $results = @(Get-ChildItem -LiteralPath $testOutput -Filter '*.trx')
    $expectedReports = if ($runStandardUser) { 3 } else { 2 }
    if ($results.Count -ne $expectedReports) { throw 'Complete unit, integration and applicable environment-category reports are required.' }
    foreach ($report in $results) {
        [xml]$xml = Get-Content -LiteralPath $report.FullName
        $counters = $xml.TestRun.ResultSummary.Counters
        if ([int]$counters.total -eq 0 -or [int]$counters.passed -ne [int]$counters.total) { throw "Test run incomplete or failed: $($report.FullName)" }
    }
    if ($env:GITHUB_STEP_SUMMARY) {
        "Native integrity: $($context.integrity). StandardUserProcessLaunch executed: $($selectionRecord.standardUserCategoryExecuted). Excluded tests are not counted as passing; see test-context.json and TRX reports." | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
} finally { Pop-Location }

