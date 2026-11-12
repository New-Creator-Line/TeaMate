# =============================================================================
# Остановка всех микросервисов Fotoprint
# =============================================================================

$ports = @(8081, 8082, 8083, 8084, 8088)

foreach ($port in $ports) {
    $conn = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue
    if ($conn) {
        $pids = $conn | Select-Object -ExpandProperty OwningProcess -Unique
        foreach ($pidToKill in $pids) {
            Stop-Process -Id $pidToKill -Force -ErrorAction SilentlyContinue
            Write-Host "Остановлен процесс PID $pidToKill на порту $port" -ForegroundColor Yellow
        }
    }
}

Write-Host "Все сервисы остановлены." -ForegroundColor Green

