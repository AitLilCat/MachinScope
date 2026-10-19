param([int]$ProcessId, [ValidateRange(5,300)][int]$Seconds = 15, [string]$Scenario = 'interactive-unspecified')
# Measures a running MachinScope session. Activity and UI state affect these readings.
$ErrorActionPreference = 'Stop'
if (!$ProcessId) {
    $running = @(Get-Process MachinScope -ErrorAction Stop)
    if ($running.Count -ne 1) { throw 'Specify -ProcessId when multiple MachinScope instances are running.' }
    $ProcessId = $running[0].Id
}
$process = Get-Process -Id $ProcessId -ErrorAction Stop
if ($process.ProcessName -ne 'MachinScope') { throw 'The selected process must be MachinScope.' }
$identity = $process.StartTime
$cpuStart = $process.TotalProcessorTime.TotalSeconds
$memory = [Collections.Generic.List[long]]::new()
$handles = [Collections.Generic.List[int]]::new()
$hardware = Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors
$physicalMemory = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
$clock = [Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt $Seconds; $i++) {
    Start-Sleep -Seconds 1
    $process.Refresh()
    if ($process.HasExited -or $process.StartTime -ne $identity) { throw 'MachinScope exited during measurement.' }
    $memory.Add($process.WorkingSet64)
    $handles.Add($process.HandleCount)
}
$elapsed = $clock.Elapsed.TotalSeconds
$report = [ordered]@{
    MeasuredAt = [DateTimeOffset]::Now.ToString('O')
    ProcessId = $ProcessId
    ProcessStarted = $identity.ToString('O')
    DurationSeconds = [Math]::Round($elapsed, 3)
    LogicalProcessors = [Environment]::ProcessorCount
    Scenario = $Scenario
    OS = [Environment]::OSVersion.VersionString
    Hardware = $hardware
    PhysicalMemoryBytes = $physicalMemory
    ProductVersion = $process.MainModule.FileVersionInfo.ProductVersion
    ExecutableSha256 = (Get-FileHash -LiteralPath $process.MainModule.FileName -Algorithm SHA256).Hash
    RuntimeTarget = 'net10.0; exact runtime available from controlled managed harness'
    AverageCpuPercentOfMachine = [Math]::Round(100 * ($process.TotalProcessorTime.TotalSeconds - $cpuStart) / $elapsed / [Environment]::ProcessorCount, 3)
    WorkingSetMinBytes = ($memory | Measure-Object -Minimum).Minimum
    WorkingSetMaxBytes = ($memory | Measure-Object -Maximum).Maximum
    HandleCountMin = ($handles | Measure-Object -Minimum).Minimum
    HandleCountMax = ($handles | Measure-Object -Maximum).Maximum
    ManagedHeapBytes = $null
    AllocationRate = $null
    ErrorCount = $null # This sampler does not observe application errors.
    Context = 'Running interactive session; not a controlled idle or scan benchmark.'
}
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\benchmarks'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$file = Join-Path $output ('session-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
$report | ConvertTo-Json | Set-Content -LiteralPath $file -Encoding UTF8
$report | ConvertTo-Json
Write-Output "Saved: $file"
