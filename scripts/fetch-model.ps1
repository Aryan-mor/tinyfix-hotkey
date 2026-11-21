# Download paperweight/tinyfix model files into models/ (not committed).
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Dest = Join-Path $RepoRoot 'models'
$PinFile = Join-Path $RepoRoot 'model-revision.txt'
if ($env:TINYFIX_MODEL_REVISION) {
    $Revision = $env:TINYFIX_MODEL_REVISION.Trim()
} elseif (Test-Path $PinFile) {
    $Revision = (Get-Content -Path $PinFile -Raw).Trim()
} else {
    $Revision = '8efdf5cd91b32584f4077aa0e8ecaf48f7651bef'
}

if ($Revision.Length -lt 40) {
    try {
        $api = Invoke-RestMethod -Uri 'https://huggingface.co/api/models/paperweight/tinyfix/revision/main' -TimeoutSec 30
        if ($api.sha -and $api.sha.StartsWith($Revision)) { $Revision = $api.sha }
    } catch {
        Write-Warning "Could not resolve revision via the Hugging Face API; using $Revision"
    }
}

New-Item -ItemType Directory -Force -Path $Dest | Out-Null
$Record = Join-Path $Dest 'REVISION.txt'
$HaveRevision = if (Test-Path $Record) { (Get-Content $Record -Raw).Trim() } else { '' }
$Refresh = $HaveRevision -ne $Revision
$Base = "https://huggingface.co/paperweight/tinyfix/resolve/$Revision"
$Files = @('wrong-word.onnx', 'wrong-word-vocab.txt', 'wrong-word-confusions.txt', 'words.txt')
foreach ($Name in $Files) {
    $Out = Join-Path $Dest $Name
    if ((-not $Refresh) -and (Test-Path $Out) -and ((Get-Item $Out).Length -gt 0)) {
        Write-Host "Already have models\$Name"
        continue
    }
    Write-Host "Downloading $Name from paperweight/tinyfix@$Revision ..."
    & curl.exe -L --fail --retry 3 --retry-delay 2 -o $Out "$Base/$Name"
    if ($LASTEXITCODE -ne 0) { throw "Failed to download $Name" }
}
Set-Content -Path $Record -Value $Revision -Encoding utf8
Write-Host "Model files are ready in $Dest (revision $Revision)"
