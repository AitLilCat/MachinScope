param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. "$PSScriptRoot\common.ps1"
Push-Location $script:RepositoryRoot
try { Invoke-CoreDotnet -Arguments @('restore','CORE.slnx','--locked-mode'); Invoke-CoreDotnet -Arguments @('build','CORE.slnx','-c',$Configuration,'--no-restore') } finally { Pop-Location }

