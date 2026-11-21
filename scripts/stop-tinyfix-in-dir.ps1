<#
.SYNOPSIS
  Stop TinyFix.exe processes whose executable path is under -AppDir.

.DESCRIPTION
  Never uses a machine-wide taskkill /IM. Processes whose path cannot be
  read, or that run from any other directory, are left alone.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppDir,

    # If set, print True/False for whether this executable path is in AppDir, then exit.
    [string]$PathCheck,

    # If set, list matching processes as JSON and do not stop them.
    [switch]$ListOnly,

    # If set, do not stop processes; exit 10 if any match, else 0.
    [switch]$RunningCheck
)

Set-StrictMode -Version 1
$ErrorActionPreference = 'Continue'

function Get-AppDirPrefix {
    param([Parameter(Mandatory = $true)][string]$Dir)
    if ([string]::IsNullOrWhiteSpace($Dir)) {
        throw 'AppDir is empty'
    }
    return [IO.Path]::GetFullPath($Dir).TrimEnd('\', '/') + '\'
}

function Test-ProcessPathInAppDir {
    param(
        [string]$ProcessPath,
        [Parameter(Mandatory = $true)][string]$AppDirPrefix
    )
    if ([string]::IsNullOrWhiteSpace($ProcessPath)) {
        return $false
    }
    try {
        $full = [IO.Path]::GetFullPath($ProcessPath)
    }
    catch {
        return $false
    }
    return $full.StartsWith($AppDirPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Get-ProcessImagePath {
    param($Process)
    $path = $null
    try { $path = $Process.Path } catch { $path = $null }
    if (-not [string]::IsNullOrWhiteSpace($path)) {
        return $path
    }
    try {
        $cim = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$($Process.Id)" -ErrorAction SilentlyContinue
        if ($cim -and $cim.ExecutablePath) {
            return [string]$cim.ExecutablePath
        }
    }
    catch {
    }
    return $null
}

function Get-TinyFixProcessesInAppDir {
    param([Parameter(Mandatory = $true)][string]$Dir)
    $prefix = Get-AppDirPrefix $Dir
    Get-Process -Name 'TinyFix' -ErrorAction SilentlyContinue | ForEach-Object {
        $path = Get-ProcessImagePath $_
        if (Test-ProcessPathInAppDir -ProcessPath $path -AppDirPrefix $prefix) {
            $_
        }
    }
}

if ($PSBoundParameters.ContainsKey('PathCheck')) {
    $prefix = Get-AppDirPrefix $AppDir
    if (Test-ProcessPathInAppDir -ProcessPath $PathCheck -AppDirPrefix $prefix) {
        Write-Output 'true'
    }
    else {
        Write-Output 'false'
    }
    exit 0
}

$matches = @(Get-TinyFixProcessesInAppDir -Dir $AppDir)
$payload = @(
    $matches | ForEach-Object {
        [pscustomobject]@{ Id = $_.Id; Path = (Get-ProcessImagePath $_) }
    }
)
function Write-PayloadJson($Items) {
    Write-Output (ConvertTo-Json -InputObject @($Items) -Compress)
}

if ($ListOnly -or $RunningCheck) {
    Write-PayloadJson $payload
    if ($RunningCheck -and $payload.Count -gt 0) { exit 10 } else { exit 0 }
}

foreach ($proc in $matches) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}

$deadline = (Get-Date).AddSeconds(8)
while ((Get-Date) -lt $deadline) {
    $still = @(Get-TinyFixProcessesInAppDir -Dir $AppDir)
    if ($still.Count -eq 0) {
        break
    }
    Start-Sleep -Milliseconds 200
}

Write-PayloadJson $payload
$remaining = @(Get-TinyFixProcessesInAppDir -Dir $AppDir)
if ($remaining.Count -gt 0) { exit 11 } else { exit 0 }
