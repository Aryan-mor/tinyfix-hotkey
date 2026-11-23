<#
  Publishes TinyFix as a self-contained, single-file, ReadyToRun build for one runtime.
    pwsh scripts/publish.ps1 -Runtime win-x64
    pwsh scripts/publish.ps1 -Runtime linux-x64 -Output artifacts/linux
  Supported runtimes: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64. Build on the target OS.

  Choices, and why:
  - self-contained: users need no .NET runtime.
  - single file + native libraries extracted on first start: one file to copy; SkiaSharp and HarfBuzz are native.
  - ReadyToRun: precompiled code, so the first start does not wait for the JIT. Not trimmed: Avalonia's XAML and
    bindings are not trim-safe enough to risk it (see docs/DOTNET-ARCHITECTURE.md).
  - The "TinyFixRemoveNativeSymbols" target of the App project deletes the native .pdb files (about 100 MB) after a
    Release publish.
#>
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$Output = "artifacts/$Runtime"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet publish src/TinyFix.App/TinyFix.App.csproj `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -o $Output
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    Write-Host "Published to $Output"
}
finally {
    Pop-Location
}
