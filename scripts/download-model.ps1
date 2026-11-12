[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$LightModel,
    [switch]$CheckOnly,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

function Show-Help {
    @"
Usage: .\scripts\download-model.ps1 [-LightModel] [-CheckOnly] [-Help]

Downloads Whisper model weights for Fotoprint local speech-to-text inference.

Parameters:
  -LightModel   Download/check lightweight model (ggml-base.bin ~142MB) instead of large-v3-turbo (~1.5GB)
  -CheckOnly    Verify whether the model exists without downloading
  -Help         Display this help message and exit
"@
}

if ($Help) {
    Show-Help
    exit 0
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$ModelsDir = Join-Path $RepoRoot "models"

$ModelName = if ($LightModel) { "ggml-base.bin" } else { "ggml-large-v3-turbo.bin" }

$LocalModelPath = Join-Path $ModelsDir $ModelName
$AppdataDir = if ($env:APPDATA) { Join-Path $env:APPDATA "app.yuko.tsukiko\models" } else { $null }
$SystemModelPath = if ($AppdataDir) { Join-Path $AppdataDir $ModelName } else { $null }

Write-Host "Checking Whisper model: $ModelName..."

$FoundPath = $null
if (Test-Path $LocalModelPath) {
    $FoundPath = $LocalModelPath
} elseif ($SystemModelPath -and (Test-Path $SystemModelPath)) {
    $FoundPath = $SystemModelPath
}

if ($FoundPath) {
    Write-Host "[OK] Whisper model found: $FoundPath" -ForegroundColor Green
    exit 0
}

if ($CheckOnly) {
    Write-Host "[WARN] Model $ModelName not found in local or system cache." -ForegroundColor Yellow
    exit 0
}

Write-Host "Model not found in local or system cache."
Write-Host "Downloading $ModelName from HuggingFace..."

if (-not (Test-Path $ModelsDir)) {
    New-Item -ItemType Directory -Path $ModelsDir -Force | Out-Null
}

$HfUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$ModelName"
$TempPath = "$LocalModelPath.tmp"

try {
    $ProgressPreference = 'SilentlyContinue'
    if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
        & curl.exe -L --fail -o $TempPath $HfUrl
        if ($LASTEXITCODE -ne 0) {
            throw "curl exited with code $LASTEXITCODE"
        }
    } else {
        Invoke-WebRequest -Uri $HfUrl -OutFile $TempPath -UseBasicParsing
    }
    Move-Item -Path $TempPath -Destination $LocalModelPath -Force
    Write-Host "[OK] Successfully downloaded $ModelName to $LocalModelPath" -ForegroundColor Green
    exit 0
} catch {
    if (Test-Path $TempPath) {
        Remove-Item -Path $TempPath -Force -ErrorAction SilentlyContinue
    }
    Write-Error "Failed to download model from $HfUrl : $_"
    exit 1
}
