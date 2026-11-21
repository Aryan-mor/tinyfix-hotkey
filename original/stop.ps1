$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$PidFile = Join-Path $Root 'tinyfix.pid'
$stopped = $false
if (Test-Path $PidFile) {
    $target = (Get-Content -Path $PidFile -Raw).Trim()
    if ($target -match '^\d+$') {
        $proc = Get-Process -Id ([int]$target) -ErrorAction SilentlyContinue
        if ($proc) {
            & taskkill.exe /PID $target /T /F | Out-Null
            $stopped = $true
        }
    }
}
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -match 'pythonw?\.exe' -and $_.CommandLine -and ($_.CommandLine -like '*\tinyfix\app.py*' -or $_.CommandLine -like '*tinyfix\\app.py*')
    } |
    ForEach-Object {
        & taskkill.exe /PID $_.ProcessId /T /F | Out-Null
        $stopped = $true
    }
if (Test-Path $PidFile) { Remove-Item $PidFile -Force -ErrorAction SilentlyContinue }
if ($stopped) { Write-Output 'TinyFix stopped.' } else { Write-Output 'TinyFix was not running.' }
