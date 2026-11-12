using System.Net;
using System.Text.Json;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Resilience;
using Fotoprint.Core.Telemetry;
using Xunit;

namespace Fotoprint.Tests;

public class GeminiClientTests
{
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? CapturedApiKeyHeader { get; private set; }
        public string? CapturedRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;

            if (request.Headers.Authorization != null)
            {
                CapturedApiKeyHeader = request.Headers.Authorization.Parameter;
            }

            if (request.Content != null)
            {
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var responseJson = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Ответ модели NVIDIA для типографии Fotoprint."
                        },
                        finish_reason = "stop"
                    }
                },
                usage = new
                {
                    prompt_tokens = 15,
                    completion_tokens = 28,
                    total_tokens = 43
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseJson), System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task GeminiClient_Tracks_Call_Count_And_Strict_15_RPM_Limit()
    {
        var handler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        var config = new FotoprintConfig();
        config.Llm.Provider = "gemini";
        config.Llm.RateLimits.Rpm = 15;
        config.Llm.Proxy.ApiKeyEnvVar = "FAKE_GEMINI_KEY";

        Environment.SetEnvironmentVariable("FAKE_GEMINI_KEY", "test-gemini-key-12345");

        var lbOptions = new LoadBalancerOptions
        {
            MaxConcurrentRequests = 5,
            MinIntervalBetweenRequestsMs = 1,
            MaxRequestsPerMinute = 15
        };
        var loadBalancer = new LlmLoadBalancer(lbOptions);
        var rollbackManager = new RollbackManager();

        var client = new GeminiClient(httpClient, config, loadBalancer, rollbackManager);

        // Начальное состояние
        var initialStats = client.GetStats();
        Assert.Equal(0, initialStats.TotalCalls);
        Assert.Equal(15, initialStats.RpmLimit);
        Assert.Equal(15, initialStats.RemainingQuotaInCurrentMinute);
        Assert.Equal(0.0, initialStats.CurrentUsagePercent);

        // Делаем 4 вызова подряд
        for (int i = 0; i < 4; i++)
        {
            var res = await client.GenerateContentAsync($"Тестовый запрос #{i + 1}");
            Assert.True(res.IsSuccess);
            Assert.Equal("Ответ модели NVIDIA для типографии Fotoprint.", res.Text);
            Assert.Equal(43, res.TotalTokens);
        }

        // Проверяем статистику счетчика
        var statsAfter4 = client.GetStats();
        Assert.Equal(4, statsAfter4.TotalCalls);
        Assert.Equal(4, statsAfter4.CallsInLast60Seconds);
        Assert.Equal(11, statsAfter4.RemainingQuotaInCurrentMinute);
        Assert.Equal(26.7, statsAfter4.CurrentUsagePercent);

        // Проверяем, что токен Bearer был передан в запросе
        Assert.Equal("test-gemini-key-12345", handler.CapturedApiKeyHeader);
    }

    [Fact]
    public async Task NvidiaClient_Sends_OpenAi_Compatible_Format_And_Parses_Response()
    {
        var openAiHandler = new FakeOpenAiMessageHandler();
        var httpClient = new HttpClient(openAiHandler);

        var config = new FotoprintConfig();
        config.Llm.Provider = "nvidia";
        config.Llm.Proxy.BaseUrl = "https://integrate.api.nvidia.com/v1";
        config.Llm.Proxy.ApiKeyEnvVar = "NVIDIA_TEST_KEY";
        config.Llm.Models.ChatModel = "nvidia/nemotron-3.5-super-vl-120b-a12b";

        Environment.SetEnvironmentVariable("NVIDIA_TEST_KEY", "nvapi-test-token-123");

        var lb = new LlmLoadBalancer(new LoadBalancerOptions { MaxConcurrentRequests = 5, MaxRequestsPerMinute = 60, MinIntervalBetweenRequestsMs = 1 });
        var rollback = new RollbackManager();

        var client = new GeminiClient(httpClient, config, lb, rollback);

        var res = await client.GenerateContentAsync("Тестовый запрос в модель NVIDIA");

        Assert.True(res.IsSuccess);
        Assert.Equal("Ответ от модели NVIDIA Nemotron", res.Text);
        Assert.Equal(50, res.TotalTokens);
        Assert.Equal("Bearer nvapi-test-token-123", openAiHandler.CapturedAuthHeader);
        Assert.Contains("chat/completions", openAiHandler.CapturedRequestUri);
        Assert.Contains("nvidia/nemotron-3.5-super-vl-120b-a12b", openAiHandler.CapturedRequestBody);
    }

    [Fact]
    public async Task NvidiaClient_Serializes_Tool_Result_Objects_And_Arrays_Without_Crashing()
    {
        var openAiHandler = new FakeOpenAiMessageHandler();
        var httpClient = new HttpClient(openAiHandler);

        var config = new FotoprintConfig();
        config.Llm.Provider = "nvidia";
        config.Llm.Proxy.BaseUrl = "https://integrate.api.nvidia.com/v1";
        config.Llm.Proxy.ApiKeyEnvVar = "NVIDIA_TEST_KEY";
        Environment.SetEnvironmentVariable("NVIDIA_TEST_KEY", "nvapi-test-token-123");

        var lb = new LlmLoadBalancer(new LoadBalancerOptions { MaxConcurrentRequests = 5, MaxRequestsPerMinute = 60, MinIntervalBetweenRequestsMs = 1 });
        var rollback = new RollbackManager();
        var client = new GeminiClient(httpClient, config, lb, rollback);

        var objResult = JsonSerializer.SerializeToElement(new { order_id = "ЗК-44102", readiness_percent = 65 });
        var arrResult = JsonSerializer.SerializeToElement(new[] { new { title = "Планерка" } });

        var contents = new List<object>
        {
            new { role = "user", content = "Статус заказа и планерки?" },
            new
            {
                role = "assistant",
                content = "",
                tool_calls = new object[]
                {
                    new { id = "call_1", type = "function", function = new { name = "get_order_status", arguments = "{\"order_id\":\"44102\"}" } }
                }
            },
            new { role = "tool", tool_call_id = "call_1", name = "get_order_status", content = objResult },
            new { role = "tool", tool_call_id = "call_2", name = "get_upcoming_meetings", content = arrResult }
        };

        var res = await client.ChatTurnAsync(contents);

        Assert.True(res.IsSuccess);
        Assert.NotNull(openAiHandler.CapturedRequestBody);
        using var doc = JsonDocument.Parse(openAiHandler.CapturedRequestBody!);
        var msgs = doc.RootElement.GetProperty("messages");
        Assert.Equal(4, msgs.GetArrayLength());

        var toolMsgObj = msgs[2];
        Assert.Equal("tool", toolMsgObj.GetProperty("role").GetString());
        Assert.Equal("call_1", toolMsgObj.GetProperty("tool_call_id").GetString());
        Assert.Equal("get_order_status", toolMsgObj.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, toolMsgObj.GetProperty("content").ValueKind);

        var toolMsgArr = msgs[3];
        Assert.Equal("tool", toolMsgArr.GetProperty("role").GetString());
        Assert.Equal("call_2", toolMsgArr.GetProperty("tool_call_id").GetString());
        Assert.Equal("get_upcoming_meetings", toolMsgArr.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, toolMsgArr.GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task ChatTurnAsync_Parses_Multiple_ToolCalls_And_NoThinking_Sets_False()
    {
        var openAiHandler = new FakeOpenAiMessageHandler
        {
            CustomResponseJson = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "",
                            tool_calls = new[]
                            {
                                new { id = "call_1", type = "function", function = new { name = "get_order_status", arguments = "{\"order_id\":\"44102\"}" } },
                                new { id = "call_2", type = "function", function = new { name = "get_upcoming_meetings", arguments = "{}" } }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                },
                usage = new { prompt_tokens = 30, completion_tokens = 20, total_tokens = 50 }
            }
        };
        var httpClient = new HttpClient(openAiHandler);
        var config = new FotoprintConfig();
        config.Llm.RateLimits.Rpm = 60;
        config.LoadBalancer.MinIntervalBetweenRequestsMs = 0;

        var lb = new LlmLoadBalancer(new LoadBalancerOptions { MaxRequestsPerMinute = 60, MinIntervalBetweenRequestsMs = 0 });
        var rollback = new RollbackManager();
        var client = new GeminiClient(httpClient, config, lb, rollback);

        var res = await client.ChatTurnAsync(new List<object> { new { role = "user", content = "Заказ и встречи" } });
        Assert.True(res.IsSuccess);
        Assert.NotNull(res.FunctionCalls);
        Assert.Equal(2, res.FunctionCalls!.Count);
        Assert.Equal("call_1", res.FunctionCalls[0].CallId);
        Assert.Equal("get_order_status", res.FunctionCalls[0].Name);
        Assert.Equal("call_2", res.FunctionCalls[1].CallId);
        Assert.Equal("get_upcoming_meetings", res.FunctionCalls[1].Name);

        await client.GenerateJsonNoThinkingAsync("Classify");
        using var reqDoc = JsonDocument.Parse(openAiHandler.CapturedRequestBody!);
        Assert.False(reqDoc.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public async Task ChatTurnAsync_Parses_Sse_Stream_Content_And_ToolCalls()
    {
        var sseBody = """
            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"Статус "},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"content":"готов."},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"c1","function":{"name":"get_order","arguments":"{\"id\":"}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"name":"_status","arguments":"\"44102\"}"}},{"index":1,"id":"c2","function":{"name":"get_user_tasks","arguments":"{}"}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

            data: {"choices":[],"usage":{"prompt_tokens":100,"completion_tokens":40,"total_tokens":140}}

            data: [DONE]
            """;

        var openAiHandler = new FakeOpenAiMessageHandler { CustomSseBody = sseBody };
        var httpClient = new HttpClient(openAiHandler);
        var config = new FotoprintConfig();
        config.LoadBalancer.MinIntervalBetweenRequestsMs = 0;

        var client = new GeminiClient(
            httpClient,
            config,
            new LlmLoadBalancer(new LoadBalancerOptions { MaxRequestsPerMinute = 60, MinIntervalBetweenRequestsMs = 0 }),
            new RollbackManager());

        var res = await client.ChatTurnAsync(new List<object> { new { role = "user", content = "test" } });

        Assert.True(res.IsSuccess);
        Assert.Equal("Статус готов.", res.Text);
        Assert.Equal("tool_calls", res.FinishReason);
        Assert.Equal(140, res.TotalTokens);
        Assert.NotNull(res.FunctionCalls);
        Assert.Equal(2, res.FunctionCalls!.Count);
        Assert.Equal("c1", res.FunctionCalls[0].CallId);
        Assert.Equal("get_order_status", res.FunctionCalls[0].Name);
        Assert.Equal("44102", res.FunctionCalls[0].Arguments.GetProperty("id").GetString());
        Assert.Equal("c2", res.FunctionCalls[1].CallId);
        Assert.Equal("get_user_tasks", res.FunctionCalls[1].Name);
    }

    [Fact]
    public async Task ChatTurnAsync_WithTools_UsesChatModel_And_NoThinking_UsesFastModel()
    {
        var prevEnvModel = Environment.GetEnvironmentVariable("LLM_MODEL");
        Environment.SetEnvironmentVariable("LLM_MODEL", null);
        try
        {
            var openAiHandler = new FakeOpenAiMessageHandler();
            var httpClient = new HttpClient(openAiHandler);

            var config = new FotoprintConfig();
            config.Llm.Provider = "nvidia";
            config.Llm.Proxy.BaseUrl = "https://integrate.api.nvidia.com/v1";
            config.Llm.Proxy.ApiKeyEnvVar = "NVIDIA_TEST_KEY";
            config.Llm.Models.ChatModel = "nvidia/nemotron-3-ultra-550b-a55b";
            config.Llm.Models.FastModel = "nvidia/nemotron-3.5-bolt-e4b";
            config.Llm.Models.FallbackModel = "nvidia/nemotron-3.5-bolt-e4b";
            Environment.SetEnvironmentVariable("NVIDIA_TEST_KEY", "nvapi-test-token-123");

            var lb = new LlmLoadBalancer(new LoadBalancerOptions { MaxRequestsPerMinute = 60, MinIntervalBetweenRequestsMs = 0 });
            var client = new GeminiClient(httpClient, config, lb, new RollbackManager());

            var tools = new[]
            {
                new
                {
                    type = "function",
                    function = new { name = "get_order_status", description = "Order", parameters = new { type = "object" } }
                }
            };

            var t1 = await client.ChatTurnAsync(new List<object> { new { role = "user", content = "Заказ 44102" } }, tools: tools);
            var t2 = await client.ChatTurnNoThinkingAsync(new List<object> { new { role = "user", content = "Синтез" } }, tools: tools);

            Assert.Equal(2, openAiHandler.CapturedRequestBodies.Count);
            using var doc1 = JsonDocument.Parse(openAiHandler.CapturedRequestBodies[0]);
            using var doc2 = JsonDocument.Parse(openAiHandler.CapturedRequestBodies[1]);

            Assert.Equal("nvidia/nemotron-3-ultra-550b-a55b", doc1.RootElement.GetProperty("model").GetString());
            Assert.True(doc1.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
            Assert.Equal("nvidia/nemotron-3-ultra-550b-a55b", t1.Model);

            Assert.Equal("nvidia/nemotron-3.5-bolt-e4b", doc2.RootElement.GetProperty("model").GetString());
            Assert.False(doc2.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
            Assert.Equal("nvidia/nemotron-3.5-bolt-e4b", t2.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_MODEL", prevEnvModel);
        }
    }

    [Fact]
    public async Task ChatTurnAsync_OpensCircuitBreaker_And_FallsBack_To_Bolt_On_503()
    {
        var prevEnvModel = Environment.GetEnvironmentVariable("LLM_MODEL");
        Environment.SetEnvironmentVariable("LLM_MODEL", null);
        try
        {
            var openAiHandler = new FakeOpenAiMessageHandler();
            openAiHandler.StatusCodesQueue.Enqueue(HttpStatusCode.ServiceUnavailable);
            var httpClient = new HttpClient(openAiHandler);

            var config = new FotoprintConfig();
            config.Llm.Provider = "nvidia";
            config.Llm.Proxy.BaseUrl = "https://integrate.api.nvidia.com/v1";
            config.Llm.Proxy.ApiKeyEnvVar = "NVIDIA_TEST_KEY";
            config.Llm.Models.ChatModel = "nvidia/nemotron-3-ultra-550b-a55b";
            config.Llm.Models.FastModel = "nvidia/nemotron-3.5-bolt-e4b";
            config.Llm.Models.FallbackModel = "nvidia/nemotron-3.5-bolt-e4b";
            Environment.SetEnvironmentVariable("NVIDIA_TEST_KEY", "nvapi-test-token-123");

            var lb = new LlmLoadBalancer(new LoadBalancerOptions { MaxRequestsPerMinute = 60, MinIntervalBetweenRequestsMs = 0 });
            var client = new GeminiClient(httpClient, config, lb, new RollbackManager());

            var firstTurn = await client.ChatTurnAsync(new List<object> { new { role = "user", content = "Запрос 1" } });
            Assert.True(firstTurn.IsSuccess);
            Assert.Equal(2, firstTurn.AttemptsMade);
            Assert.Equal("nvidia/nemotron-3.5-bolt-e4b", firstTurn.Model);

            var secondTurn = await client.ChatTurnAsync(new List<object> { new { role = "user", content = "Запрос 2" } });
            Assert.True(secondTurn.IsSuccess);
            Assert.Equal(1, secondTurn.AttemptsMade);
            Assert.Equal("nvidia/nemotron-3.5-bolt-e4b", secondTurn.Model);

            var requestedModels = openAiHandler.CapturedRequestBodies
                .Select(b => JsonDocument.Parse(b).RootElement.GetProperty("model").GetString())
                .ToList();

            Assert.Equal(
                new[]
                {
                    "nvidia/nemotron-3-ultra-550b-a55b",
                    "nvidia/nemotron-3.5-bolt-e4b",
                    "nvidia/nemotron-3.5-bolt-e4b"
                },
                requestedModels);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_MODEL", prevEnvModel);
        }
    }

    [Fact]
    public async Task ChatTurnAsync_OpensCircuitBreaker_And_FallsBack_To_Bolt_On_HeaderTimeout()
    {
        var prevEnvModel = Environment.GetEnvironmentVariable("LLM_MODEL");
        Environment.SetEnvironmentVariable("LLM_MODEL", null);
        try
        {
            var openAiHandler = new FakeOpenAiMessageHandler();
            openAiHandler.ExceptionsQueue.Enqueue(new TaskCanceledException("Simulated TTFT header timeout"));
            var httpClient = new HttpClient(openAiHandler);

            var config = new FotoprintConfig();
            config.Llm.Provider = "nvidia";
            config.Llm.Proxy.BaseUrl = "https://integrate.api.nvidia.com/v1";
            config.Llm.Proxy.ApiKeyEnvVar = "NVIDIA_TEST_KEY";
            config.Llm.Models.ChatModel = "nvidia/nemotron-3-ultra-550b-a55b";
            config.Llm.Models.FastModel = "nvidia/nemotron-3.5-bolt-e4b";
            config.Llm.Models.FallbackModel = "nvidia/nemotron-3.5-bolt-e4b";
            Environment.SetEnvironmentVariable("NVIDIA_TEST_KEY", "nvapi-test-token-123");

            var lb = new LlmLoadBalancer(new LoadBalancerOptions { MaxRequestsPerMinute = 60, MinIntervalBetweenRequestsMs = 0 });
            var client = new GeminiClient(httpClient, config, lb, new RollbackManager());

            var firstTurn = await client.ChatTurnAsync(new List<object> { new { role = "user", content = "Запрос с таймаутом заголовков" } });
            Assert.True(firstTurn.IsSuccess);
            Assert.Equal(2, firstTurn.AttemptsMade);
            Assert.Equal("nvidia/nemotron-3.5-bolt-e4b", firstTurn.Model);

            var secondTurn = await client.ChatTurnAsync(new List<object> { new { role = "user", content = "Последующий запрос при открытом Circuit Breaker" } });
            Assert.True(secondTurn.IsSuccess);
            Assert.Equal(1, secondTurn.AttemptsMade);
            Assert.Equal("nvidia/nemotron-3.5-bolt-e4b", secondTurn.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_MODEL", prevEnvModel);
        }
    }

    private class FakeOpenAiMessageHandler : HttpMessageHandler
    {
        public string? CapturedAuthHeader { get; private set; }
        public string? CapturedRequestUri { get; private set; }
        public string? CapturedRequestBody { get; private set; }
        public object? CustomResponseJson { get; set; }
        public string? CustomSseBody { get; set; }
        public List<string> CapturedRequestBodies { get; } = new();
        public Queue<HttpStatusCode> StatusCodesQueue { get; } = new();
        public Queue<Exception> ExceptionsQueue { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequestUri = request.RequestUri?.ToString();
            CapturedAuthHeader = request.Headers.Authorization?.ToString();
            if (request.Content != null)
            {
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (CapturedRequestBody != null)
            {
                CapturedRequestBodies.Add(CapturedRequestBody);
            }

            if (ExceptionsQueue.Count > 0)
            {
                throw ExceptionsQueue.Dequeue();
            }

            if (StatusCodesQueue.Count > 0)
            {
                var status = StatusCodesQueue.Dequeue();
                if (status != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(status)
                    {
                        Content = new StringContent("{\"error\":{\"message\":\"upstream unavailable\"}}", System.Text.Encoding.UTF8, "application/json")
                    };
                }
            }

            if (CustomSseBody != null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CustomSseBody, System.Text.Encoding.UTF8, "text/event-stream")
                };
            }

            var responseJson = CustomResponseJson ?? new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Ответ от модели NVIDIA Nemotron"
                        },
                        finish_reason = "stop"
                    }
                },
                usage = new
                {
                    prompt_tokens = 20,
                    completion_tokens = 30,
                    total_tokens = 50
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseJson), System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task GeminiClient_InteractionsApi_Supports_FunctionCall_And_MultiTurn()
    {
        var interactionsHandler = new FakeInteractionsMessageHandler();
        var httpClient = new HttpClient(interactionsHandler);

        var config = new FotoprintConfig();
        config.Llm.Provider = "gemini";
        config.Llm.Proxy.BaseUrl = "https://generativelanguage.googleapis.com/v1beta/interactions";
        config.Llm.Proxy.ApiKeyEnvVar = "GEMINI_TEST_KEY";
        config.Llm.Models.ChatModel = "gemma-4-31b-it";
        config.Llm.Models.FastModel = "gemini-3.5-flash-lite";

        Environment.SetEnvironmentVariable("GEMINI_TEST_KEY", "test-interactions-key");

        var lb = new LlmLoadBalancer(new LoadBalancerOptions { MaxConcurrentRequests = 5, MaxRequestsPerMinute = 60, MinIntervalBetweenRequestsMs = 1 });
        var rollback = new RollbackManager();
        var client = new GeminiClient(httpClient, config, lb, rollback);

        // Turn 1: User asks for order status, model triggers function_call
        var contents = new List<object>
        {
            new { role = "user", content = "Узнай статус заказа 123" }
        };
        var tools = new[]
        {
            new
            {
                type = "function",
                name = "get_order_status",
                description = "Get status",
                parameters = new { type = "object", properties = new { order_id = new { type = "string" } } }
            }
        };

        var traceId = "trace-test-interactions-1";
        var turn1 = await client.ChatTurnAsync(contents, tools, "Ты ассистент", traceId: traceId);

        Assert.True(turn1.IsSuccess);
        Assert.NotNull(turn1.FunctionCall);
        Assert.Equal("get_order_status", turn1.FunctionCall.Name);
        Assert.Equal("call_12345", turn1.FunctionCall.CallId);
        Assert.Equal("tool_calls", turn1.FinishReason);
        Assert.Equal("test-interactions-key", interactionsHandler.CapturedApiKey);
        Assert.Equal("2026-05-20", interactionsHandler.CapturedApiRevision);
        Assert.DoesNotContain("chat/completions", interactionsHandler.CapturedRequestUri);

        // Turn 2: Provide tool result
        contents.Add(new
        {
            role = "assistant",
            content = "",
            tool_calls = new[]
            {
                new { id = "call_12345", type = "function", function = new { name = "get_order_status", arguments = "{}" } }
            }
        });
        contents.Add(new
        {
            role = "tool",
            tool_call_id = "call_12345",
            name = "get_order_status",
            content = "{\"status\":\"В печати\"}"
        });

        var turn2 = await client.ChatTurnNoThinkingAsync(contents, null, "Ты ассистент", traceId: traceId);

        Assert.True(turn2.IsSuccess);
        Assert.Equal("Заказ 123 находится в печати.", turn2.Text);
        Assert.Equal("stop", turn2.FinishReason);
        Assert.Contains("previous_interaction_id", interactionsHandler.LastCapturedBody);
        Assert.Contains("v1_interaction_test_id", interactionsHandler.LastCapturedBody);
    }

    private class FakeInteractionsMessageHandler : HttpMessageHandler
    {
        public string? CapturedApiKey { get; private set; }
        public string? CapturedApiRevision { get; private set; }
        public string? CapturedRequestUri { get; private set; }
        public string? LastCapturedBody { get; private set; }
        public int TurnCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TurnCount++;
            CapturedRequestUri = request.RequestUri?.ToString();

            if (request.Headers.TryGetValues("x-goog-api-key", out var keyVals))
            {
                CapturedApiKey = keyVals.FirstOrDefault();
            }
            if (request.Headers.TryGetValues("Api-Revision", out var revVals))
            {
                CapturedApiRevision = revVals.FirstOrDefault();
            }

            if (request.Content != null)
            {
                LastCapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            object responseObj;
            if (TurnCount == 1)
            {
                responseObj = new
                {
                    id = "v1_interaction_test_id",
                    status = "completed",
                    steps = new object[]
                    {
                        new { type = "thought", content = new[] { new { type = "text", text = "Думаю..." } } },
                        new { type = "function_call", id = "call_12345", name = "get_order_status", arguments = new { order_id = "123" } }
                    },
                    usage = new { total_input_tokens = 25, total_output_tokens = 10, total_tokens = 35 }
                };
            }
            else
            {
                responseObj = new
                {
                    id = "v1_interaction_test_id_turn2",
                    status = "completed",
                    steps = new object[]
                    {
                        new { type = "model_output", content = new[] { new { type = "text", text = "Заказ 123 находится в печати." } } }
                    },
                    usage = new { total_input_tokens = 40, total_output_tokens = 15, total_tokens = 55 }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseObj), System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

}
