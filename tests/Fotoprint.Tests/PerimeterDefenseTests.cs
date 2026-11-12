using System.Net;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fotoprint.Tests;

public class PerimeterDefenseTests
{
    private class FakeLangfuseClient : ILangfuseClient
    {
        public List<string> SecurityAlerts { get; } = new();

        public string GenerateTraceId() => "trace-security-test-123";

        public Task CreateTraceAsync(string traceId, string name, string? userId = null, string? sessionId = null, object? metadata = null, string[]? tags = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task LogEventAsync(string traceId, string name, string level = "DEFAULT", string? message = null, object? metadata = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task LogSecurityAlertAsync(string traceId, long telegramId, string toolOrAction, string requiredPermission, string reason, CancellationToken ct = default)
        {
            SecurityAlerts.Add($"{toolOrAction}:{requiredPermission}:{reason}");
            return Task.CompletedTask;
        }

        public Task LogScoreAsync(string traceId, string name, double value, string? comment = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    [Theory]
    [InlineData("sqlmap/1.6#stable")]
    [InlineData("Mozilla/5.0 (compatible; Nmap Scripting Engine; https://nmap.org/book/nse.html)")]
    [InlineData("masscan/1.3")]
    [InlineData("Nikto/2.1.6")]
    public async Task PerimeterMiddleware_Blocks_Known_Port_Scanners_By_UserAgent(string scannerUserAgent)
    {
        var config = new FotoprintConfig();
        var langfuse = new FakeLangfuseClient();
        bool nextCalled = false;

        var middleware = new PerimeterDefenseMiddleware(
            next: ctx => { nextCalled = true; return Task.CompletedTask; },
            config: config,
            logger: NullLogger<PerimeterDefenseMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Headers.UserAgent = scannerUserAgent;

        await middleware.InvokeAsync(context, langfuse);

        Assert.False(nextCalled);
        Assert.Equal((int)HttpStatusCode.NotFound, context.Response.StatusCode);
        Assert.Contains(langfuse.SecurityAlerts, a => a.Contains("ScannerBlocked"));
    }

    [Theory]
    [InlineData("/.env")]
    [InlineData("/.git/config")]
    [InlineData("/wp-admin/setup-config.php")]
    [InlineData("/phpmyadmin/index.php")]
    [InlineData("/actuator/health")]
    [InlineData("/cgi-bin/test.cgi")]
    public async Task PerimeterMiddleware_Blocks_Honeypot_Probe_Paths(string probePath)
    {
        var config = new FotoprintConfig();
        var langfuse = new FakeLangfuseClient();
        bool nextCalled = false;

        var middleware = new PerimeterDefenseMiddleware(
            next: ctx => { nextCalled = true; return Task.CompletedTask; },
            config: config,
            logger: NullLogger<PerimeterDefenseMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = probePath;
        context.Request.Headers.UserAgent = "Mozilla/5.0";

        await middleware.InvokeAsync(context, langfuse);

        Assert.False(nextCalled);
        Assert.Equal((int)HttpStatusCode.NotFound, context.Response.StatusCode);
        Assert.Contains(langfuse.SecurityAlerts, a => a.Contains("ProbePathBlocked"));
    }

    [Fact]
    public async Task PerimeterMiddleware_Allows_Legitimate_Requests()
    {
        var config = new FotoprintConfig();
        var langfuse = new FakeLangfuseClient();
        bool nextCalled = false;

        var middleware = new PerimeterDefenseMiddleware(
            next: ctx => { nextCalled = true; return Task.CompletedTask; },
            config: config,
            logger: NullLogger<PerimeterDefenseMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/telegram/webhook";
        context.Request.Headers.UserAgent = "TelegramBot (like TwitterBot)";

        await middleware.InvokeAsync(context, langfuse);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
        Assert.Empty(langfuse.SecurityAlerts);
    }

    [Fact]
    public async Task InternalServiceAuthMiddleware_Blocks_Direct_MCP_Calls_Without_Secret_Key()
    {
        var config = new FotoprintConfig();
        var langfuse = new FakeLangfuseClient();
        bool nextCalled = false;

        var middleware = new InternalServiceAuthMiddleware(
            next: ctx => { nextCalled = true; return Task.CompletedTask; },
            config: config,
            logger: NullLogger<InternalServiceAuthMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/tools/get_order_status";
        // Заголовок X-Internal-Service-Key не передан!

        await middleware.InvokeAsync(context, langfuse);

        Assert.False(nextCalled);
        Assert.Equal((int)HttpStatusCode.Forbidden, context.Response.StatusCode);
        Assert.Contains(langfuse.SecurityAlerts, a => a.Contains("DirectMcpAccessBlocked"));
    }

    [Fact]
    public async Task InternalServiceAuthMiddleware_Allows_Calls_With_Valid_Internal_Service_Key()
    {
        var config = new FotoprintConfig();
        var langfuse = new FakeLangfuseClient();
        bool nextCalled = false;

        var middleware = new InternalServiceAuthMiddleware(
            next: ctx => { nextCalled = true; return Task.CompletedTask; },
            config: config,
            logger: NullLogger<InternalServiceAuthMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/tools/get_order_status";
        context.Request.Headers[InternalServiceAuthMiddleware.InternalHeaderName] = config.Security.InternalServiceSecretKey;

        await middleware.InvokeAsync(context, langfuse);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
        Assert.Empty(langfuse.SecurityAlerts);
    }

    [Fact]
    public void InternalServiceAuthMiddleware_IsAuthorized_ValidatesHeaderUsingConstantTimeMatch()
    {
        var config = new FotoprintConfig();
        var validContext = new DefaultHttpContext();
        validContext.Request.Headers[InternalServiceAuthMiddleware.InternalHeaderName] = config.Security.InternalServiceSecretKey;
        Assert.True(InternalServiceAuthMiddleware.IsAuthorized(validContext.Request, config));

        var missingContext = new DefaultHttpContext();
        Assert.False(InternalServiceAuthMiddleware.IsAuthorized(missingContext.Request, config));

        var invalidContext = new DefaultHttpContext();
        invalidContext.Request.Headers[InternalServiceAuthMiddleware.InternalHeaderName] = "wrong-secret-key";
        Assert.False(InternalServiceAuthMiddleware.IsAuthorized(invalidContext.Request, config));

        config.Security.EnforceInternalServiceAuth = false;
        Assert.True(InternalServiceAuthMiddleware.IsAuthorized(missingContext.Request, config));
    }

    [Fact]
    public async Task InternalServiceAuthMiddleware_Allows_Health_Check_Without_Secret_Key()
    {
        var config = new FotoprintConfig();
        var langfuse = new FakeLangfuseClient();
        bool nextCalled = false;

        var middleware = new InternalServiceAuthMiddleware(
            next: ctx => { nextCalled = true; return Task.CompletedTask; },
            config: config,
            logger: NullLogger<InternalServiceAuthMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context, langfuse);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
        Assert.Empty(langfuse.SecurityAlerts);
    }
}

