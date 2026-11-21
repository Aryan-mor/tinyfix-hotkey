$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Pythonw = Join-Path $Root 'venv\Scripts\pythonw.exe'
$App = Join-Path $Root 'app.py'
if (-not (Test-Path $Pythonw)) { throw "Missing $Pythonw" }
Start-Process -FilePath $Pythonw -ArgumentList @("`"$App`"") -WorkingDirectory $Root -WindowStyle Hidden
