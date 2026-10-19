param([string]$OutputDirectory)
. "$PSScriptRoot\common.ps1"
$output = if (!$OutputDirectory) { Join-Path $script:RepositoryRoot 'artifacts\MachinScope-single-exe' } elseif ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $script:RepositoryRoot $OutputDirectory }
Push-Location $script:RepositoryRoot
try {
    Invoke-CoreDotnet -Arguments @('publish', 'src\Core.Desktop\Core.Desktop.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:WindowsAppSDKSelfContained=true', '-p:EnableMsixTooling=true', '-p:PublishSingleFile=true', '-p:IncludeAllContentForSelfExtract=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:NuGetLockFilePath=packages.portable.lock.json', '-p:RestorePackagesWithLockFile=true', '-p:RestoreLockedMode=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $output)
    if (!(Test-Path -LiteralPath (Join-Path $output 'MachinScope.exe'))) { throw 'Single-file MachinScope.exe is missing.' }
    Write-Output "Single-file portable executable: $(Join-Path $output 'MachinScope.exe')"
} finally { Pop-Location }
