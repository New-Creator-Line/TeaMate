# =============================================================================
# Fotoprint AI Copilot - Local Windows Startup (.NET 9 + Whisper Vulkan/CPU)
# =============================================================================
# Требует запущенных в Docker (или локально) PostgreSQL, n8n и Langfuse.
# =============================================================================

$ErrorActionPreference = 'Stop'
$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

# 1. Загружаем переменные окружения из .env (если есть)
$envFile = Join-Path $RepoRoot ".env"
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith('#')) { return }
        $idx = $line.IndexOf('=')
        if ($idx -gt 0) {
            $k = $line.Substring(0, $idx).Trim()
            $v = $line.Substring($idx + 1).Trim().Trim('"').Trim("'")
            [Environment]::SetEnvironmentVariable($k, $v, 'Process')
        }
    }
    Write-Host "[ENV] Loaded variables from .env" -ForegroundColor Gray
}

# 2. Локальные значения по умолчанию для Windows-запуска без Docker для .NET
$pgPort = if ($env:POSTGRES_PORT) { $env:POSTGRES_PORT } else { "5432" }
$pgUser = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { "fotoprint" }
$pgPass = if ($env:POSTGRES_PASSWORD) { $env:POSTGRES_PASSWORD } else { "fotoprint_secret" }
$pgDb   = if ($env:POSTGRES_DB) { $env:POSTGRES_DB } else { "fotoprint" }

if (-not $env:ConnectionStrings__Postgres) {
    $env:ConnectionStrings__Postgres = "Host=localhost;Port=$pgPort;Database=$pgDb;Username=$pgUser;Password=$pgPass"
}
if (-not $env:N8n__WebhookBaseUrl)        { $env:N8n__WebhookBaseUrl        = "http://localhost:5678/webhook" }
if (-not $env:Langfuse__BaseUrl)          { $env:Langfuse__BaseUrl          = "http://localhost:3000" }
if (-not $env:McpUrls__EmployeeReader)    { $env:McpUrls__EmployeeReader    = "http://localhost:8081" }
if (-not $env:McpUrls__OneC)              { $env:McpUrls__OneC              = "http://localhost:8082" }
if (-not $env:McpUrls__Bitrix24)          { $env:McpUrls__Bitrix24          = "http://localhost:8083" }
if (-not $env:McpUrls__Calendar)          { $env:McpUrls__Calendar          = "http://localhost:8084" }
if (-not $env:McpUrls__KnowledgeBase)     { $env:McpUrls__KnowledgeBase     = "http://localhost:8086" }
if (-not $env:Security__EmployeeReaderUrl){ $env:Security__EmployeeReaderUrl= "http://localhost:8081" }

# 3. Проверка модели Whisper
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "scripts\download-model.ps1") -CheckOnly

# 4. Сборка решения перед запуском
Write-Host "`nBuilding Fotoprint.slnx..." -ForegroundColor Cyan
dotnet build Fotoprint.slnx -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed. Fix compilation errors before starting services."
    exit 1
}

$projects = @(
    @{ Name = "Fotoprint.Mcp.EmployeeReader"; Port = 8081; Path = "src/Fotoprint.Mcp.EmployeeReader" },
    @{ Name = "Fotoprint.Mcp.OneC";           Port = 8082; Path = "src/Fotoprint.Mcp.OneC" },
    @{ Name = "Fotoprint.Mcp.Bitrix24";       Port = 8083; Path = "src/Fotoprint.Mcp.Bitrix24" },
    @{ Name = "Fotoprint.Mcp.Calendar";       Port = 8084; Path = "src/Fotoprint.Mcp.Calendar" },
    @{ Name = "Fotoprint.Mcp.KnowledgeBase";  Port = 8086; Path = "src/Fotoprint.Mcp.KnowledgeBase" },
    @{ Name = "Fotoprint.AiGateway.Api";      Port = 8088; Path = "src/Fotoprint.AiGateway.Api" }
)

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "Starting Fotoprint AI Copilot Services (.NET 9, Windows)..." -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

foreach ($p in $projects) {
    Write-Host "Starting $($p.Name) on port $($p.Port)..." -ForegroundColor Yellow
    Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project $($p.Path) -c Release --no-build --urls http://localhost:$($p.Port)" `
        -WorkingDirectory $RepoRoot `
        -WindowStyle Hidden
}

Write-Host "`nWaiting for services to initialize (6 seconds)..." -ForegroundColor Gray
Start-Sleep -Seconds 6

Write-Host "`nChecking service health endpoints:" -ForegroundColor Cyan
foreach ($p in $projects) {
    try {
        $res = Invoke-RestMethod -Uri "http://localhost:$($p.Port)/health" -TimeoutSec 3 -ErrorAction Stop
        Write-Host "  [OK] $($p.Name) (:$($p.Port)) -> ONLINE ($($res.status))" -ForegroundColor Green
    } catch {
        Write-Host "  [WAIT] $($p.Name) (:$($p.Port)) -> Starting up..." -ForegroundColor Yellow
    }
}

Write-Host "`n=========================================================" -ForegroundColor Cyan
Write-Host "Local AI Gateway + Web UI (with Whisper STT): http://localhost:8088" -ForegroundColor Green
Write-Host "=========================================================" -ForegroundColor Cyan
