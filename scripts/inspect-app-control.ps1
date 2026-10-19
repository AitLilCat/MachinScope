# Read-only diagnostics. Run from an elevated PowerShell window.
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Open PowerShell as administrator and rerun this script. It only reads configuration.'
}
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\app-control-diagnostics'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$tool = Join-Path $env:windir 'System32\CiTool.exe'
$policyJson = & $tool --list-policies --json
if ($LASTEXITCODE -ne 0) { throw "Policy inventory failed: $policyJson" }
$policyJson | Set-Content -LiteralPath (Join-Path $output 'policies.json') -Encoding UTF8
$state = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction SilentlyContinue
[pscustomobject]@{ Collected = [DateTimeOffset]::Now; SmartAppControlState = $state.VerifiedAndReputablePolicyState } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'state.json') -Encoding UTF8
Write-Output "Read-only reports saved to $output. No policy or security setting changed."
