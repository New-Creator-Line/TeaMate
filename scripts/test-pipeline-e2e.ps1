param(
    [string]$N8nBaseUrl = "http://127.0.0.1:5678",
    [string]$GrafanaUrl = "http://127.0.0.1:3001",
    [string]$GrafanaUser = "admin",
    [string]$GrafanaPass = "FotoprintGrafana2026!",
    [int]$TimeoutSec = 180
)

$ErrorActionPreference = "Stop"

$testCases = @(
    @{ Id = 1;  Name = "1C: Статус заказа (Директор)";        Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 200; Body = @{ user_id = 100001; chat_id = 100001; text = "Какой статус заказа ЗК-44102?" } },
    @{ Id = 2;  Name = "1C: Производственные KPI (Директор)"; Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 200; Body = @{ user_id = 100001; chat_id = 100001; text = "Покажи текущие производственные KPI и загрузку цеха" } },
    @{ Id = 3;  Name = "Bitrix24: Сделки и клиенты (Продажи)"; Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 200; Body = @{ user_id = 100003; chat_id = 100003; text = "Какие активные сделки в Bitrix24 сейчас в работе?" } },
    @{ Id = 4;  Name = "Bitrix24: Мои задачи (Производство)"; Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 200; Body = @{ user_id = 100002; chat_id = 100002; text = "Какие задачи на мне висят в Bitrix24?" } },
    @{ Id = 5;  Name = "Calendar: Ближайшие встречи";          Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 200; Body = @{ user_id = 100001; chat_id = 100001; text = "Какие у меня запланированы созвоны и встречи?" } },
    @{ Id = 6;  Name = "KnowledgeBase: Регламенты печати";    Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 200; Body = @{ user_id = 100002; chat_id = 100002; text = "Какие требования к макетам для офсетной печати по регламенту?" } },
    @{ Id = 7;  Name = "Бухгалтерия: Выручка/долги (100004)";  Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 200; Body = @{ user_id = 100004; chat_id = 100004; text = "Есть ли просроченная дебиторка или проблемы с оплатой по заказам?" } },
    @{ Id = 8;  Name = "Context Switch: Смена темы (100001)"; Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 200; Body = @{ user_id = 100001; chat_id = 100001; text = "А что по поставщикам бумаги и остаткам на складе?" } },
    @{ Id = 9;  Name = "Pipeline 08: Meeting RAG Search";     Endpoint = "/webhook/meeting-recall"; ExpectedStatus = 200; Body = @{ telegram_id = 100001; question = "Какие решения приняли по каталогам 44102?" } },
    @{ Id = 10; Name = "RBAC Auth: Неизвестный юзер (403)";   Endpoint = "/webhook/fotoprint-chat"; ExpectedStatus = 403; Body = @{ user_id = 999999; chat_id = 999999; text = "Покажи секретные отчёты" } }
)

$testStartNs = [int64](([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) * 1000000000)
$results = @()

Write-Host "=== Запуск E2E интеграционного теста пайплайна (10 запросов) ===" -ForegroundColor Cyan

foreach ($tc in $testCases) {
    $url = "$N8nBaseUrl$($tc.Endpoint)"
    $jsonBody = $tc.Body | ConvertTo-Json -Compress -Depth 5
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $statusCode = 0
    $respObj = $null
    $errMsg = ""

    try {
        $resp = Invoke-WebRequest -Uri $url -Method POST -Body $jsonBody -ContentType "application/json; charset=utf-8" -TimeoutSec $TimeoutSec -UseBasicParsing
        $statusCode = [int]$resp.StatusCode
        try { $respObj = $resp.Content | ConvertFrom-Json } catch {}
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $rawErr = $reader.ReadToEnd()
            try { $respObj = $rawErr | ConvertFrom-Json } catch { $errMsg = $rawErr }
        }
        else {
            $errMsg = $_.Exception.Message
        }
    }
    catch {
        $errMsg = $_.Exception.Message
    }
    finally {
        $sw.Stop()
    }

    $elapsedMs = [math]::Round($sw.Elapsed.TotalMilliseconds)
    $passed = ($statusCode -eq $tc.ExpectedStatus)
    if ($passed -and $tc.ExpectedStatus -eq 200 -and $respObj -and $respObj.PSObject.Properties["is_success"] -and $respObj.is_success -eq $false) {
        $passed = $false
        $errMsg = "is_success=false"
    }

    $tools = if ($respObj -and $respObj.tools_invoked) { ($respObj.tools_invoked -join ",") } else { "-" }
    $tokens = if ($respObj -and $respObj.total_tokens) { [int]$respObj.total_tokens } else { 0 }
    $branch = if ($respObj -and $respObj.branch) { $respObj.branch } else { "-" }
    $traceId = if ($respObj -and $respObj.trace_id) { $respObj.trace_id } else { "-" }

    $results += [PSCustomObject]@{
        Id       = $tc.Id
        Name     = $tc.Name
        Status   = $statusCode
        Expected = $tc.ExpectedStatus
        Passed   = $passed
        TimeMs   = $elapsedMs
        Branch   = $branch
        Tools    = $tools
        Tokens   = $tokens
        TraceId  = $traceId
        Error    = $errMsg
    }

    $color = if ($passed) { "Green" } else { "Red" }
    Write-Host ("[{0:D2}/10] {1} -> HTTP {2} ({3} ms) | branch={4} | tools={5} | tokens={6} | {7}" -f $tc.Id, $tc.Name, $statusCode, $elapsedMs, $branch, $tools, $tokens, $(if ($passed) { "PASS" } else { "FAIL: $errMsg" })) -ForegroundColor $color
}

$testEndNs = [int64](([DateTimeOffset]::UtcNow.AddSeconds(3).ToUnixTimeSeconds()) * 1000000000)

Write-Host "`n=== Сводная статистика ===" -ForegroundColor Cyan
$results | Format-Table Id, Name, Status, Passed, TimeMs, Branch, Tools, Tokens -AutoSize

$passedCount = ($results | Where-Object { $_.Passed }).Count
$failedCount = $results.Count - $passedCount
$times = ($results | Select-Object -ExpandProperty TimeMs) | Sort-Object
$avgMs = [math]::Round(($times | Measure-Object -Average).Average)
$minMs = $times[0]
$maxMs = $times[-1]
$medianMs = $times[[math]::Floor($times.Count / 2)]

$aiTimes = ($results | Where-Object { $_.Expected -eq 200 -and $_.Endpoint -ne "/webhook/meeting-recall" } | Select-Object -ExpandProperty TimeMs)
$avgAiMs = if ($aiTimes) { [math]::Round(($aiTimes | Measure-Object -Average).Average) } else { 0 }

Write-Host "Пройдено: $passedCount / $($results.Count) | Провалено: $failedCount"
Write-Host "Время ответа (все 10): min=${minMs} ms, avg=${avgMs} ms, median=${medianMs} ms, max=${maxMs} ms"
Write-Host "Среднее время AI-запросов (01 + 08): ${avgAiMs} ms"

# Check Loki for any errors during test execution
Start-Sleep -Seconds 3
try {
    $pair = "${GrafanaUser}:${GrafanaPass}"
    $b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
    $hdr = @{ Authorization = "Basic $b64" }
    $q = [uri]::EscapeDataString('{service=~"ai-gateway|mcp-.*|n8n|clickhouse|clickhouse_query_error|langfuse"} |~ "(?i)(fail:|crit:|err:|unhandled|exception|500)"')
    $lokiResp = Invoke-RestMethod "$GrafanaUrl/api/datasources/proxy/uid/loki/loki/api/v1/query_range?query=$q&start=$testStartNs&end=$testEndNs&limit=30&direction=backward" -Headers $hdr
    $errLines = @()
    foreach ($stream in $lokiResp.data.result) {
        $svc = $stream.stream.service
        foreach ($val in $stream.values) {
            $errLines += "[$svc] $($val[1])"
        }
    }
    Write-Host "`n=== Ошибки в Loki за время теста: $($errLines.Count) ===" -ForegroundColor Yellow
    $errLines | Select-Object -First 15 | ForEach-Object { Write-Host $_ }
}
catch {
    Write-Host "Не удалось проверить Loki: $($_.Exception.Message)" -ForegroundColor Yellow
}

if ($failedCount -gt 0) { exit 1 }

