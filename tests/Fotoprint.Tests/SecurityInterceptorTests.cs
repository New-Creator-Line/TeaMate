using System.Net;
using System.Text.Json;
using Fotoprint.Core.Mcp;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fotoprint.Tests;

public class SecurityInterceptorTests
{
    private class FakeSecurityValidator : ISecurityValidator
    {
        public Task<(bool IsAllowed, EmployeeSecurityContext? Context, string? Reason)> ValidateAccessAsync(
            long telegramId, string requiredPermission, string actionName, string? traceId = null, CancellationToken ct = default)
        {
            if (telegramId == 100001) // Director
            {
                var ctx = new EmployeeSecurityContext
                {
                    EmployeeId = 1,
                    FullName = "Смирнов Алексей",
                    TelegramId = 100001,
                    Roles = new() { "Director" },
                    Permissions = new() { requiredPermission }
                };
                return Task.FromResult((true, (EmployeeSecurityContext?)ctx, (string?)null));
            }

            if (telegramId == 100002 && requiredPermission == "onec:read_orders") // Production
            {
                var ctx = new EmployeeSecurityContext
                {
                    EmployeeId = 2,
                    FullName = "Кузнецов Иван",
                    TelegramId = 100002,
                    Roles = new() { "Production" },
                    Permissions = new() { "onec:read_orders" }
                };
                return Task.FromResult((true, (EmployeeSecurityContext?)ctx, (string?)null));
            }

            return Task.FromResult<(bool IsAllowed, EmployeeSecurityContext? Context, string? Reason)>((false, null, "Access Denied: Missing required permission"));
        }
    }

    private class FakeLangfuseClient : ILangfuseClient
    {
        public string GenerateTraceId() => "fake-trace-id-123";
        public Task CreateTraceAsync(string traceId, string name, string? userId = null, string? sessionId = null, object? metadata = null, string[]? tags = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogEventAsync(string traceId, string name, string level = "DEFAULT", string? message = null, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogSecurityAlertAsync(string traceId, long telegramId, string toolOrAction, string requiredPermission, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogScoreAsync(string traceId, string name, double value, string? comment = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Interceptor_Blocks_Unauthorized_User_With_403()
    {
        var middleware = new SecurityInterceptorMiddleware(
            next: (innerCtx) => Task.CompletedTask,
            logger: NullLogger<SecurityInterceptorMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Telegram-Id"] = "100002"; // Production worker

        // Endpoint requires executive permission
        var endpoint = new Endpoint(
            requestDelegate: (c) => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new RequirePermissionAttribute("executive:view_summary")),
            displayName: "TestExecutiveSummaryEndpoint"
        );
        context.SetEndpoint(endpoint);

        var validator = new FakeSecurityValidator();
        var langfuse = new FakeLangfuseClient();

        await middleware.InvokeAsync(context, validator, langfuse);

        Assert.Equal((int)HttpStatusCode.Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Interceptor_Allows_Authorized_User()
    {
        bool nextCalled = false;
        var middleware = new SecurityInterceptorMiddleware(
            next: (innerCtx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            logger: NullLogger<SecurityInterceptorMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Telegram-Id"] = "100001"; // Director

        var endpoint = new Endpoint(
            requestDelegate: (c) => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new RequirePermissionAttribute("executive:view_summary")),
            displayName: "TestExecutiveSummaryEndpoint"
        );
        context.SetEndpoint(endpoint);

        var validator = new FakeSecurityValidator();
        var langfuse = new FakeLangfuseClient();

        await middleware.InvokeAsync(context, validator, langfuse);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
        Assert.NotNull(context.Items["SecurityContext"]);
    }

    [Fact]
    public async Task Interceptor_Blocks_Mutating_Endpoint_Without_User_Confirmation()
    {
        bool nextCalled = false;
        var middleware = new SecurityInterceptorMiddleware(
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            logger: NullLogger<SecurityInterceptorMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Telegram-Id"] = "100001";
        context.Request.Headers["X-User-Confirmed"] = "false";

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(
                new RequirePermissionAttribute("rag:search_all"),
                new RequireConfirmationAttribute(true)),
            displayName: "TestIndexDocumentEndpoint"
        );
        context.SetEndpoint(endpoint);

        await middleware.InvokeAsync(context, new FakeSecurityValidator(), new FakeLangfuseClient());

        Assert.False(nextCalled);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("confirmation_required", body);
    }

    [Fact]
    public async Task Interceptor_Allows_Mutating_Endpoint_With_User_Confirmation()
    {
        bool nextCalled = false;
        var middleware = new SecurityInterceptorMiddleware(
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            logger: NullLogger<SecurityInterceptorMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Telegram-Id"] = "100001";
        context.Request.Headers["X-User-Confirmed"] = "true";

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(
                new RequirePermissionAttribute("rag:search_all"),
                new RequireConfirmationAttribute(true)),
            displayName: "TestIndexDocumentEndpoint"
        );
        context.SetEndpoint(endpoint);

        await middleware.InvokeAsync(context, new FakeSecurityValidator(), new FakeLangfuseClient());

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
    }
}
