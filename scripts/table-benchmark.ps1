param([string]$Report = 'artifacts/performance-phase5/after.json', [ValidateSet('legacy','optimized')][string]$Algorithm = 'optimized')
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-CoreDotnet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot 'tests/Core.Benchmarks'), '-c', 'Release', '--', (Join-Path $script:RepositoryRoot $Report), $Algorithm)
$reportPath = Join-Path $script:RepositoryRoot $Report
$record = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$record | Add-Member -NotePropertyName hardware -NotePropertyValue @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
$record | Add-Member -NotePropertyName physicalMemoryBytes -NotePropertyValue (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
$record | Add-Member -NotePropertyName harnessSha256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $script:RepositoryRoot 'tests/Core.Benchmarks/Program.cs') -Algorithm SHA256).Hash
$record | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding utf8
