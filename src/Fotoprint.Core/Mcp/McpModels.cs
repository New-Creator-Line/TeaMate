using System.Text.Json.Serialization;

namespace Fotoprint.Core.Mcp;

public class McpJsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; } = 1;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonRpcParams? Params { get; set; }
}

public class JsonRpcParams
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?>? Arguments { get; set; }
}

public class McpJsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; } = 1;

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

public class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public class McpToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; } = new { type = "object", properties = new { } };
}

public class McpToolCallResult
{
    [JsonPropertyName("content")]
    public List<McpContentItem> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    public static McpToolCallResult Text(string text, bool isError = false) => new()
    {
        IsError = isError,
        Content = new() { new McpContentItem { Type = "text", Text = text } }
    };
}

public class McpContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

