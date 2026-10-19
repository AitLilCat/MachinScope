param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. "$PSScriptRoot\common.ps1"
$testOutput = Join-Path $script:RepositoryRoot ('artifacts\tests\' + [Guid]::NewGuid().ToString('N'))
Push-Location $script:RepositoryRoot
try {
    Invoke-CoreDotnet -Arguments @('test','CORE.slnx','-c',$Configuration,'--logger','trx','--results-directory',$testOutput)
    $results = @(Get-ChildItem -LiteralPath $testOutput -Filter '*.trx')
    if ($results.Count -lt 2) { throw 'Both unit and integration test reports are required.' }
    foreach ($report in $results) {
        [xml]$xml = Get-Content -LiteralPath $report.FullName
        $counters = $xml.TestRun.ResultSummary.Counters
        if ([int]$counters.total -eq 0 -or [int]$counters.passed -ne [int]$counters.total) { throw "Test run incomplete or failed: $($report.FullName)" }
    }
} finally { Pop-Location }

