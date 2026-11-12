using System.Text;
using System.Text.Json.Serialization;
using Fotoprint.Core.Audit;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Documents;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Mcp;
using Fotoprint.Core.Models;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var appConfig = FotoprintConfigLoader.Load();
builder.Services.AddSingleton(appConfig);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=fotoprint;Username=fotoprint;Password=fotoprint_secret";
var employeeReaderUrl = builder.Configuration["Security:EmployeeReaderUrl"] ?? "http://localhost:8081";
var langfuseBaseUrl = builder.Configuration["Langfuse:BaseUrl"] ?? "http://localhost:3000";
var langfusePubKey = builder.Configuration["Langfuse:PublicKey"] ?? "pk-lf-fotoprint-local";
var langfuseSecKey = builder.Configuration["Langfuse:SecretKey"] ?? "sk-lf-fotoprint-local";

builder.Services.AddSingleton<IFotoprintRepository>(_ => new FotoprintRepository(connectionString));
builder.Services.AddSingleton<IAuditLog>(sp => new PostgresAuditLog(connectionString, sp.GetService<ILogger<PostgresAuditLog>>()));
builder.Services.AddSingleton<IDocumentParser, DocumentParser>();

builder.Services.AddHttpClient("GeminiEmbeddingHttpClient", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<IEmbeddingClient>(sp => new GeminiEmbeddingClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeminiEmbeddingHttpClient"),
    sp.GetRequiredService<FotoprintConfig>(),
    sp.GetService<ILogger<GeminiEmbeddingClient>>()));

builder.Services.AddHttpClient<ILangfuseClient, LangfuseClient>();
builder.Services.AddSingleton(new LangfuseOptions
{
    BaseUrl = langfuseBaseUrl,
    PublicKey = langfusePubKey,
    SecretKey = langfuseSecKey,
    Enabled = true
});

builder.Services.AddHttpClient<ISecurityValidator, SecurityValidator>(client =>
{
    client.BaseAddress = new Uri(employeeReaderUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("X-Internal-Service-Key", appConfig.Security.InternalServiceSecretKey);
});

var app = builder.Build();
AuditTrail.Instance = app.Services.GetRequiredService<IAuditLog>();

// Защита периметра, аутентификация сервиса и RBAC + Human-in-the-Loop
app.UseMiddleware<PerimeterDefenseMiddleware>();
app.UseMiddleware<InternalServiceAuthMiddleware>();
app.UseMiddleware<SecurityInterceptorMiddleware>();

// Health
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "MCP-KnowledgeBase",
    storage_table = "corporate_document_chunks",
    timestamp = DateTime.UtcNow
}));

var tools = new List<McpToolDefinition>
{
    new()
    {
        Name = "search_typography_regulations",
        Description = "Ищет в корпоративной базе знаний (corporate_documents / corporate_document_chunks) регламенты типографии, техкарты Heidelberg, коммерческую политику, ПВТР и внутренние инструкции с учетом RBAC-групп сотрудника.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "Поисковый запрос по базе знаний или '*' для списка доступных документов" },
                top_k = new { type = "integer", description = "Максимум возвращаемых фрагментов (по умолчанию из config.yaml)" }
            },
            required = new[] { "query" }
        }
    },
    new()
    {
        Name = "index_corporate_document",
        Description = "Добавляет и векторизует новый корпоративный регламент в таблицу corporate_documents / corporate_document_chunks (требует подтверждения).",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                title = new { type = "string", description = "Название регламента" },
                content = new { type = "string", description = "Полный текст документа" },
                department = new { type = "string", description = "Отдел-владелец (по умолчанию Общий)" },
                allowed_access_groups = new { type = "array", items = new { type = "string" }, description = "Группы доступа RBAC (например ['all'] или ['sales', 'c-level'])" }
            },
            required = new[] { "title", "content" }
        }
    }
};

app.MapGet("/tools/list", () => Results.Ok(new { tools }));

// Tool 1: search_typography_regulations (только регламенты и база знаний, без созвонов)
app.MapGet("/tools/search_typography_regulations", [RequirePermission("rag:search_department")] [RequireConfirmation(false)] async (
    HttpContext httpContext,
    IFotoprintRepository repo,
    IEmbeddingClient embeddingClient,
    FotoprintConfig config,
    ILangfuseClient langfuse,
    CancellationToken ct,
    string query = "",
    int? top_k = null) =>
{
    var secContext = httpContext.Items["SecurityContext"] as EmployeeSecurityContext;
    var userAccessGroups = secContext?.AccessGroups?.Count > 0
        ? secContext.AccessGroups
        : new List<string> { "all" };

    var effectiveTopK = top_k is > 0 and <= 20
        ? top_k.Value
        : (config.Rag.TopK > 0 ? config.Rag.TopK : 5);

    List<CorporateDocumentChunk>? chunks = null;
    var searchMode = "text";

    if (embeddingClient.IsConfigured && !FotoprintRepository.IsGeneralCatalogQuery(query))
    {
        var queryEmbedding = await embeddingClient.EmbedTextAsync(query, "RETRIEVAL_QUERY", ct);
        if (queryEmbedding != null)
        {
            chunks = await repo.SearchSimilarChunksAsync(queryEmbedding, userAccessGroups, effectiveTopK, ct);
            if (chunks.Count > 0)
            {
                searchMode = "vector";
            }
        }
    }

    if (chunks == null || chunks.Count == 0)
    {
        chunks = await repo.SearchTextChunksAsync(query, userAccessGroups, effectiveTopK, ct);
        searchMode = FotoprintRepository.IsGeneralCatalogQuery(query) ? "catalog" : "text";
    }

    var traceId = httpContext.Request.Headers["X-Trace-Id"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(traceId))
    {
        await langfuse.LogEventAsync(traceId, "MCP:KnowledgeBase:SearchRegulations", "INFO",
            $"RAG search '{query}' (mode={searchMode}, matches={chunks.Count}, groups=[{string.Join(", ", userAccessGroups)}])", ct: ct);
    }

    if (chunks.Count > 0)
    {
        return Results.Ok(new
        {
            status = "ok",
            source = "MCP-KnowledgeBase",
            storage_table = "corporate_document_chunks",
            search_mode = searchMode,
            query,
            user_access_groups = userAccessGroups,
            matches_count = chunks.Count,
            chunks = chunks.Select(c => new
            {
                chunk_id = c.Id,
                document_id = c.DocumentId,
                chunk_index = c.ChunkIndex,
                text = c.ChunkText
            }).ToList()
        });
    }

    return Results.Ok(new
    {
        status = "no_matches_or_restricted",
        source = "MCP-KnowledgeBase",
        storage_table = "corporate_document_chunks",
        search_mode = searchMode,
        query,
        user_access_groups = userAccessGroups,
        matches_count = 0,
        chunks = Array.Empty<object>(),
        message = "В корпоративной базе знаний (RAG) не найдено фрагментов по данному запросу для ваших групп доступа."
    });
});

// Tool 2: index_corporate_document (индексация регламента с обязательным Human-in-the-Loop подтверждением)
app.MapPost("/tools/index_corporate_document", [RequirePermission("rag:search_all")] [RequireConfirmation(true)] async (
    [FromBody] IndexCorporateDocumentRequest req,
    HttpContext httpContext,
    IFotoprintRepository repo,
    IDocumentParser docParser,
    IEmbeddingClient embeddingClient,
    FotoprintConfig config,
    ILangfuseClient langfuse,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Content))
    {
        return Results.BadRequest(new { status = "error", message = "Необходимо указать title и content регламента." });
    }

    var rawGroups = req.AllowedAccessGroups is { Count: > 0 }
        ? req.AllowedAccessGroups
        : req.AllowedAccessGroupsCamel;
    var allowedGroups = rawGroups is { Count: > 0 }
        ? rawGroups
        : new List<string> { "all" };

    var chunkSize = config.Rag.Chunking.ChunkSize > 0 ? config.Rag.Chunking.ChunkSize : 800;
    var chunkOverlap = config.Rag.Chunking.ChunkOverlap >= 0 ? config.Rag.Chunking.ChunkOverlap : 150;

    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(req.Content));
    var parsed = await docParser.ParseStreamAsync(stream, $"{req.Title}.txt", chunkSize, chunkOverlap, ct);
    var chunkTexts = parsed.Chunks.Select(c => c.Text).ToList();
    if (chunkTexts.Count == 0)
    {
        chunkTexts.Add(req.Content.Trim());
    }

    var embeddings = await embeddingClient.EmbedBatchAsync(chunkTexts, "RETRIEVAL_DOCUMENT", ct);

    var docId = await repo.SaveDocumentAsync(
        title: req.Title.Trim(),
        sourceType: "corporate_regulation",
        filePath: null,
        department: string.IsNullOrWhiteSpace(req.Department) ? "Общий" : req.Department.Trim(),
        allowedAccessGroups: allowedGroups,
        metadata: new { indexed_by = "MCP-KnowledgeBase", created_utc = DateTime.UtcNow },
        ct: ct);

    await repo.SaveDocumentChunksAsync(docId, chunkTexts, embeddings, allowedGroups, ct);

    var traceId = httpContext.Request.Headers["X-Trace-Id"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(traceId))
    {
        await langfuse.LogEventAsync(traceId, "MCP:KnowledgeBase:IndexDocument", "INFO",
            $"Регламент '{req.Title}' (doc_id={docId}) проиндексирован: {embeddings.Count(e => e != null)}/{chunkTexts.Count} чанков.", ct: ct);
    }

    return Results.Ok(new
    {
        status = "indexed",
        source = "MCP-KnowledgeBase",
        storage_table = "corporate_document_chunks",
        document_id = docId,
        title = req.Title.Trim(),
        chunks_count = chunkTexts.Count,
        embedded_chunks_count = embeddings.Count(e => e != null),
        allowed_access_groups = allowedGroups
    });
});

app.Run();

public record IndexCorporateDocumentRequest(
    string Title,
    string Content,
    string? Department = null,
    [property: JsonPropertyName("allowed_access_groups")] List<string>? AllowedAccessGroups = null,
    [property: JsonPropertyName("allowedAccessGroups")] List<string>? AllowedAccessGroupsCamel = null);

