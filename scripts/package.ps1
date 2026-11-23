<#
  Turns one `scripts/publish.ps1` output into the release files for that runtime.
    pwsh scripts/package.ps1 -Runtime win-x64 -Version 2.0.0
  Run it on the same OS you published (Inno Setup, tar permissions, hdiutil).
  macOS images are ad-hoc signed only. They are not notarized.
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime,
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$PublishDir = "",
    [string]$Output = "artifacts/dist"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $root "artifacts/$Runtime"
}
$Output = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

$publishedName = if ($Runtime -like 'win-*') { 'TinyFix.exe' } else { 'TinyFix' }
$published = Join-Path $PublishDir $publishedName
if (-not (Test-Path -LiteralPath $published)) {
    throw "Published binary not found: $published"
}

function Copy-LfText([string]$Source, [string]$Destination) {
    $text = [System.IO.File]::ReadAllText($Source) -replace "`r`n", "`n" -replace "`r", "`n"
    if (-not $text.EndsWith("`n")) { $text += "`n" }
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Destination, $text, $utf8)
}

function Assert-Exit([string]$What) {
    if ($LASTEXITCODE -ne 0) { throw "$What failed ($LASTEXITCODE)" }
}

if ($Runtime -eq 'win-x64') {
    if (-not $IsWindows) { throw "Windows packages have to be built on Windows." }
    $portable = Join-Path $Output "TinyFix-$Version-win-x64.exe"
    Copy-Item -LiteralPath $published -Destination $portable -Force
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command iscc -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }
    if (-not $iscc) { throw "Inno Setup compiler (ISCC.exe) was not found. Install it (choco install innosetup) and re-run." }
    & $iscc "/DAppVersion=$Version" "/DSourceDir=$PublishDir" "/DOutputDir=$Output" "/DRepoRoot=$root" (Join-Path $root 'installer\windows\tinyfix.iss')
    Assert-Exit 'ISCC'
    return
}

if ($Runtime -like 'linux-*') {
    if (-not $IsLinux) { throw "Linux archives have to be built on Linux so tar keeps the executable bit." }
    $arch = $Runtime.Substring('linux-'.Length)
    $stage = Join-Path $Output "stage-$Runtime"
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath $published -Destination (Join-Path $stage 'TinyFix')
    Copy-LfText (Join-Path $root 'installer\linux\install.sh') (Join-Path $stage 'install.sh')
    Copy-LfText (Join-Path $root 'installer\linux\uninstall.sh') (Join-Path $stage 'uninstall.sh')
    Copy-LfText (Join-Path $root 'installer\linux\README.md') (Join-Path $stage 'README.md')
    Copy-LfText (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE')
    Copy-LfText (Join-Path $root 'THIRD-PARTY-NOTICES.md') (Join-Path $stage 'THIRD-PARTY-NOTICES.md')

    # Optional: a png from the ico. No icon in the archive when ImageMagick cannot read it.
    $ico = Join-Path $root 'assets\tinyfix.ico'
    $iconDir = Join-Path $stage 'icon-tmp'
    New-Item -ItemType Directory -Path $iconDir | Out-Null
    $converter = $null
    foreach ($name in @('magick', 'convert')) {
        if (Get-Command $name -ErrorAction SilentlyContinue) { $converter = $name; break }
    }
    if ($converter) {
        & $converter $ico '-background' 'none' "PNG32:$(Join-Path $iconDir 'frame.png')"
        $frames = @(Get-ChildItem -LiteralPath $iconDir -Filter '*.png' -ErrorAction SilentlyContinue)
        if ($LASTEXITCODE -eq 0 -and $frames.Count -gt 0) {
            $best = $frames | Sort-Object Length -Descending | Select-Object -First 1
            Copy-Item -LiteralPath $best.FullName -Destination (Join-Path $stage 'tinyfix.png')
        }
        else {
            Write-Host "No tray icon in the archive: $converter could not read assets/tinyfix.ico."
        }
    }
    else {
        Write-Host "No tray icon in the archive: ImageMagick is not installed."
    }
    Remove-Item -LiteralPath $iconDir -Recurse -Force

    foreach ($name in @('TinyFix', 'install.sh', 'uninstall.sh')) {
        & chmod +x (Join-Path $stage $name)
        Assert-Exit "chmod $name"
    }
    $archive = Join-Path $Output "TinyFix-$Version-linux-$arch.tar.gz"
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    $names = @('TinyFix', 'install.sh', 'uninstall.sh', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')
    if (Test-Path -LiteralPath (Join-Path $stage 'tinyfix.png')) { $names += 'tinyfix.png' }
    & tar -C $stage -czf $archive @names
    Assert-Exit 'tar'
    Remove-Item -LiteralPath $stage -Recurse -Force
    return
}

if ($Runtime -like 'osx-*') {
    if (-not $IsMacOS) { throw "macOS images have to be built on macOS (codesign, hdiutil)." }
    $arch = $Runtime.Substring('osx-'.Length)
    $stage = Join-Path $Output "stage-$Runtime"
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    $app = Join-Path $stage 'TinyFix.app'
    $macOsDir = Join-Path $app 'Contents/MacOS'
    $resDir = Join-Path $app 'Contents/Resources'
    New-Item -ItemType Directory -Path $macOsDir, $resDir | Out-Null
    Copy-Item -LiteralPath $published -Destination (Join-Path $macOsDir 'TinyFix')
    & chmod +x (Join-Path $macOsDir 'TinyFix')
    Assert-Exit 'chmod TinyFix'

    $plist = [System.IO.File]::ReadAllText((Join-Path $root 'installer\macos\Info.plist'))
    $plist = $plist.Replace('@VERSION@', $Version)
    # An icns only when macOS can turn the ico into a png. Otherwise the bundle has no icon.
    $png = Join-Path $stage 'icon.png'
    & sips -s format png (Join-Path $root 'assets\tinyfix.ico') --out $png
    $haveIcon = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $png)
    if ($haveIcon) {
        $iconset = Join-Path $stage 'TinyFix.iconset'
        New-Item -ItemType Directory -Path $iconset | Out-Null
        Copy-Item -LiteralPath $png -Destination (Join-Path $iconset 'icon_256x256.png')
        & iconutil -c icns $iconset -o (Join-Path $resDir 'TinyFix.icns')
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath (Join-Path $resDir 'TinyFix.icns'))) {
            $plist = $plist.Replace('<key>LSUIElement</key>', "<key>CFBundleIconFile</key>`n  <string>TinyFix</string>`n  <key>LSUIElement</key>")
        }
        else {
            Write-Host "No app icon: iconutil could not build an icns."
        }
    }
    else {
        Write-Host "No app icon: sips could not read assets/tinyfix.ico."
    }
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Join-Path $app 'Contents/Info.plist'), $plist.Replace("`r`n", "`n"), $utf8)

    # Ad-hoc signature so the bundle is sealed. Not notarized: Gatekeeper will warn.
    & codesign --force --deep -s - $app
    Assert-Exit 'codesign'

    $dmgRoot = Join-Path $stage 'dmg'
    New-Item -ItemType Directory -Path $dmgRoot | Out-Null
    # Copy-Item -Recurse drops nested files on some hosts; cp keeps the bundle intact.
    & cp -R $app (Join-Path $dmgRoot 'TinyFix.app')
    Assert-Exit 'cp TinyFix.app'
    $dmg = Join-Path $Output "TinyFix-$Version-osx-$arch.dmg"
    if (Test-Path -LiteralPath $dmg) { Remove-Item -LiteralPath $dmg -Force }
    & hdiutil create -volname TinyFix -srcfolder $dmgRoot -ov -format UDZO $dmg
    Assert-Exit 'hdiutil'
    Remove-Item -LiteralPath $stage -Recurse -Force
    return
}

throw "Unsupported runtime $Runtime"
