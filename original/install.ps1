$ErrorActionPreference = 'Stop'
$Root = 'D:\tools\tinyfix'
$Python = 'D:\Python312\python.exe'
if (-not (Test-Path 'D:\')) { throw 'Drive D: was not found. Install aborted.' }
if (-not (Test-Path $Python)) { throw "Expected Python at $Python" }
if (-not (Test-Path (Join-Path $Root 'app.py'))) { throw "TinyFix files are missing from $Root" }

New-Item -ItemType Directory -Force -Path (Join-Path $Root 'cache\pip') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Root 'cache\tmp') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Root 'cache\hf') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Root 'cache\pycache') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Root 'models') | Out-Null

$env:PIP_CACHE_DIR = Join-Path $Root 'cache\pip'
$env:HF_HOME = Join-Path $Root 'cache\hf'
$env:HUGGINGFACE_HUB_CACHE = Join-Path $Root 'cache\hf'
$env:TMP = Join-Path $Root 'cache\tmp'
$env:TEMP = Join-Path $Root 'cache\tmp'
$env:PYTHONPYCACHEPREFIX = Join-Path $Root 'cache\pycache'
$env:PIP_USER = '0'

Write-Output 'Creating virtual environment on D: ...'
& $Python -m venv (Join-Path $Root 'venv')
$VenvPython = Join-Path $Root 'venv\Scripts\python.exe'
if (-not (Test-Path $VenvPython)) { throw 'venv python was not created' }

Write-Output 'Installing Python packages into the venv ...'
& $VenvPython -m pip install --upgrade --disable-pip-version-check --no-user pip
& $VenvPython -m pip install --disable-pip-version-check --no-user -r (Join-Path $Root 'requirements.txt')

$Revision = '8efdf5cd91b32584f4077aa0e8ecaf48f7651bef'
$Base = "https://huggingface.co/paperweight/tinyfix/resolve/$Revision"
$Files = @('wrong-word.onnx','wrong-word-vocab.txt','wrong-word-confusions.txt','words.txt')
foreach ($Name in $Files) {
    $Dest = Join-Path $Root "models\$Name"
    if (Test-Path $Dest) {
        Write-Output "Already have models\$Name"
        continue
    }
    $Url = "$Base/$Name"
    Write-Output "Downloading $Name ..."
    & curl.exe -L --fail --retry 3 --retry-delay 2 -o $Dest $Url
    if ($LASTEXITCODE -ne 0) { throw "Failed to download $Name" }
}

Write-Output 'Creating Startup shortcut ...'
$Startup = [Environment]::GetFolderPath('Startup')
$ShortcutPath = Join-Path $Startup 'TinyFix.lnk'
$Wsh = New-Object -ComObject WScript.Shell
$Shortcut = $Wsh.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = Join-Path $Root 'venv\Scripts\pythonw.exe'
$Shortcut.Arguments = '"' + (Join-Path $Root 'app.py') + '"'
$Shortcut.WorkingDirectory = $Root
$Shortcut.WindowStyle = 7
$Shortcut.Description = 'TinyFix: select text and press F3 to correct English typos'
$Shortcut.Save()

Write-Output 'Install finished.'
Write-Output "Shortcut: $ShortcutPath"
