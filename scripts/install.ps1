param([Parameter(Mandatory=$true)][string]$DistributionDirectory, [string]$Destination = "$env:LOCALAPPDATA\Programs\MachinScope")
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $DistributionDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $source 'MachinScope.exe'))) { throw 'Distribution must contain MachinScope.exe and its companion files.' }
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists. Close and uninstall the previous version first.' }
New-Item -ItemType Directory -Path $Destination | Out-Null
Get-ChildItem -LiteralPath $source | Copy-Item -Destination $Destination -Recurse
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'MachinScope.lnk'))
$shortcut.TargetPath = Join-Path $Destination 'MachinScope.exe'; $shortcut.WorkingDirectory = $Destination; $shortcut.Save()
Write-Output "Installed for the current user: $Destination"
