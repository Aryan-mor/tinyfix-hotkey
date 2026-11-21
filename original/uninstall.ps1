$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $Root 'stop.ps1')
$Startup = [Environment]::GetFolderPath('Startup')
$Shortcut = Join-Path $Startup 'TinyFix.lnk'
if (Test-Path $Shortcut) { Remove-Item $Shortcut -Force }
Write-Output "Stopped the helper and removed the Startup shortcut."
Write-Output "To finish uninstalling, delete this folder after you close any open files in it:"
Write-Output "  $Root"
