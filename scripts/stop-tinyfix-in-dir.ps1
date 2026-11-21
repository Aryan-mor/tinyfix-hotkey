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
    [switch]$ListOnly
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

function Get-TinyFixProcessesInAppDir {
    param([Parameter(Mandatory = $true)][string]$Dir)
    $prefix = Get-AppDirPrefix $Dir
    Get-Process -Name 'TinyFix' -ErrorAction SilentlyContinue | ForEach-Object {
        $path = $null
        try { $path = $_.Path } catch { $path = $null }
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
        $path = $null
        try { $path = $_.Path } catch { $path = $null }
        [pscustomobject]@{ Id = $_.Id; Path = $path }
    }
)
function Write-PayloadJson($Items) {
    Write-Output (ConvertTo-Json -InputObject @($Items) -Compress)
}

if ($ListOnly) {
    Write-PayloadJson $payload
    exit 0
}

foreach ($proc in $matches) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}
Write-PayloadJson $payload
