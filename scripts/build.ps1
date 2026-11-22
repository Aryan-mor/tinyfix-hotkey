# Build TinyFix-Setup.exe: fetch model, package with PyInstaller, wrap with Inno Setup.
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

function Find-Python {
    foreach ($candidate in @('D:\Python312\python.exe', 'D:\Python313\python.exe')) {
        if (Test-Path $candidate) { return $candidate }
    }
    $cmd = Get-Command py -ErrorAction SilentlyContinue
    if ($cmd) {
        $resolved = & py -3.12 -c "import sys; print(sys.executable)" 2>$null
        if ($LASTEXITCODE -eq 0 -and $resolved) { return $resolved.Trim() }
        $resolved = & py -3 -c "import sys; print(sys.executable)" 2>$null
        if ($LASTEXITCODE -eq 0 -and $resolved) { return $resolved.Trim() }
    }
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw 'Python 3.10+ was not found. Install Python or set PATH.'
}

$BuildTools = if (Test-Path 'D:\tools') { 'D:\tools\build' } else { Join-Path $RepoRoot '.build-tools' }
New-Item -ItemType Directory -Force -Path $BuildTools | Out-Null

Write-Host '==> Fetching model files'
& (Join-Path $PSScriptRoot 'fetch-model.ps1')

$Python = Find-Python
Write-Host "==> Using Python at $Python"
& $Python -c "import sys; assert sys.version_info >= (3, 10), sys.version"

$Venv = Join-Path $BuildTools 'tinyfix-hotkey-venv'
$VenvPython = Join-Path $Venv 'Scripts\python.exe'
if (-not (Test-Path $VenvPython)) {
    Write-Host "==> Creating build venv at $Venv"
    & $Python -m venv $Venv
}
$env:PIP_CACHE_DIR = Join-Path $BuildTools 'pip-cache'
$env:PIP_USER = '0'
$env:PYINSTALLER_CONFIG_DIR = Join-Path $BuildTools 'pyinstaller-cache'
New-Item -ItemType Directory -Force -Path $env:PYINSTALLER_CONFIG_DIR | Out-Null
Write-Host '==> Installing build dependencies'
& $VenvPython -m pip install --upgrade --disable-pip-version-check --no-user pip
& $VenvPython -m pip install --disable-pip-version-check --no-user -r (Join-Path $RepoRoot 'requirements-dev.txt')

Write-Host '==> Creating icon'
& $VenvPython (Join-Path $PSScriptRoot 'make_icon.py')
# Also place a copy next to the packaged app sources for the tray/installer
Copy-Item -Force (Join-Path $RepoRoot 'assets\tinyfix.ico') (Join-Path $RepoRoot 'tinyfix.ico') -ErrorAction SilentlyContinue

Write-Host '==> Running PyInstaller'
$Dist = Join-Path $RepoRoot 'dist'
$Work = Join-Path $RepoRoot 'build'
if (Test-Path (Join-Path $Dist 'TinyFix')) { Remove-Item -Recurse -Force (Join-Path $Dist 'TinyFix') }
& $VenvPython -m PyInstaller --noconfirm --clean (Join-Path $RepoRoot 'tinyfix.spec')
$Bundled = Join-Path $Dist 'TinyFix\TinyFix.exe'
if (-not (Test-Path $Bundled)) { throw "PyInstaller did not produce $Bundled" }

# Copy icon and a default config template is written by the installer, not here.
Copy-Item -Force (Join-Path $RepoRoot 'assets\tinyfix.ico') (Join-Path $Dist 'TinyFix\tinyfix.ico')

$Iscc = $null
foreach ($candidate in @(
    (Join-Path $BuildTools 'InnoSetup\ISCC.exe'),
    'D:\tools\build\InnoSetup\ISCC.exe',
    $env:ISCC
)) {
    if ($candidate -and (Test-Path $candidate)) { $Iscc = $candidate; break }
}
if (-not $Iscc) {
    Write-Host '==> Inno Setup not found; downloading a portable install into the build-tools folder'
    $InnoSetupDir = Join-Path $BuildTools 'InnoSetup'
    $InnoInstaller = Join-Path $BuildTools 'innosetup-7.1.1-x64.exe'
    if (-not (Test-Path $InnoInstaller)) {
        & curl.exe -L --fail --retry 3 --retry-delay 2 -o $InnoInstaller 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe'
        if ($LASTEXITCODE -ne 0) { throw 'Failed to download Inno Setup' }
    }
    New-Item -ItemType Directory -Force -Path $InnoSetupDir | Out-Null
    $p = Start-Process -FilePath $InnoInstaller -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', '/SP-', '/TASKS=',
        "/DIR=$InnoSetupDir"
    ) -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Inno Setup installer exited $($p.ExitCode)" }
    $Iscc = Join-Path $InnoSetupDir 'ISCC.exe'
}
if (-not (Test-Path $Iscc)) { throw "ISCC.exe was not found at $Iscc" }

Write-Host "==> Compiling installer with $Iscc"
& $Iscc (Join-Path $RepoRoot 'installer\tinyfix.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$Setup = Join-Path $Dist 'TinyFix-Setup.exe'
if (-not (Test-Path $Setup)) { throw "Installer was not produced: $Setup" }
Write-Host "Build finished: $Setup"
Get-Item $Setup | Format-List FullName, Length, LastWriteTime

# The optional offline pack (numpy + onnxruntime + model) is a separate release asset, built with the
# same Python as the exe because the compiled parts must match. It is published once to the
# "offline-pack" release and is not rebuilt for every app version.
Write-Host '==> Building the optional offline pack'
& $VenvPython (Join-Path $PSScriptRoot 'build_pack.py') --out (Join-Path $Dist 'pack')
if ($LASTEXITCODE -ne 0) { throw 'Offline pack build failed' }

Write-Host '==> Writing SHA256 checksums'
$Hashes = @()
$ToHash = @($Setup) + @(Get-ChildItem (Join-Path $Dist 'pack') -Filter *.zip | ForEach-Object { $_.FullName })
foreach ($file in $ToHash) {
    $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLower()
    $Hashes += "$h  $(Split-Path -Leaf $file)"
}
$Hashes | Set-Content -Encoding ascii (Join-Path $Dist 'SHA256SUMS.txt')
$Hashes

