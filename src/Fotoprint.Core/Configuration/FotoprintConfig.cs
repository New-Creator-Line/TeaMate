using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// =============================================================================
// Fotoprint AI Copilot — C#-модель и загрузчик для config.yaml
// =============================================================================
// ВНИМАНИЕ: config.yaml — это ЕДИНСТВЕННЫЙ источник конфигурации в системе.
// Данный C#-класс является строго типизированной объектной моделью (POCO/DTO),
// в которую десериализуется config.yaml для работы компилятора, DI и IntelliSense.
// Значения свойств по умолчанию служат исключительно как страховочный fallback (fail-safe)
// на случай отсутствия ключа в YAML или для легковесных юнит-тестов в памяти.
// =============================================================================

namespace Fotoprint.Core.Configuration;

/// <summary>
/// Корневой объект конфигурации. Загружается из config.yaml через FotoprintConfigLoader.
/// </summary>
public class FotoprintConfig
{
    public string Version { get; set; } = "1.0";
    public EnvironmentConfig Environment { get; set; } = new();
    public LlmConfig Llm { get; set; } = new();
    public LoadBalancerConfig LoadBalancer { get; set; } = new();
    public ResilienceConfig Resilience { get; set; } = new();
    public RagConfig Rag { get; set; } = new();
    public MeetingsConfig Meetings { get; set; } = new();
    public TelemetryConfig Telemetry { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public AgentConfig Agent { get; set; } = new();
    public ContextSwitchConfig ContextSwitch { get; set; } = new();
    public AudioConfig Audio { get; set; } = new();
    public NotificationsConfig Notifications { get; set; } = new();
    public McpUrlsConfig McpUrls { get; set; } = new();
    public CalendarConfig Calendar { get; set; } = new();
}

public class NotificationsConfig
{
    public bool Enabled { get; set; } = true;
    public int DigestHourMsk { get; set; } = 9;
    public int PreMeetingWindowMinutes { get; set; } = 30;
    public int TaskDueSoonHours { get; set; } = 24;
    public int TaskUrgentHours { get; set; } = 2;
    public bool SaveNotificationsToSession { get; set; } = true;
    public bool IncludeEmptyMorningDigest { get; set; } = true;
    public int TaskMaxOverdueDays { get; set; } = 30;
    public int MaxTaskAlertsPerRunPerEmployee { get; set; } = 5;
}

public class McpUrlsConfig
{
    public string? EmployeeReader { get; set; } = "http://localhost:8081";
    public string? OneC { get; set; } = "http://localhost:8082";
    public string? Bitrix24 { get; set; } = "http://localhost:8083";
    public string? Calendar { get; set; } = "http://localhost:8084";
}

public class CalendarConfig
{
    public bool Enabled { get; set; } = true;
    public string ServiceUrl { get; set; } = "http://localhost:8084";
    public string Timezone { get; set; } = "Europe/Moscow";
    public string WorkingHoursStart { get; set; } = "09:00";
    public string WorkingHoursEnd { get; set; } = "18:00";
    public int DefaultBufferMinutes { get; set; } = 10;
    public int DefaultSlotDurationMinutes { get; set; } = 30;
}

public class AudioConfig
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "auto";
    public string VocabularyPath { get; set; } = "config/vocabulary.yaml";
    public LocalEngineAudioConfig Engine { get; set; } = new();
    public GeminiAudioConfig Gemini { get; set; } = new();
    public int MaxFileSizeMb { get; set; } = 100;
}

public class LocalEngineAudioConfig
{
    public int TimeoutSeconds { get; set; } = 1800;
}

public class GeminiAudioConfig
{
    public string Model { get; set; } = "gemini-2.5-flash";
}

public class ContextSwitchConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxHistoryMessages { get; set; } = 10;
    public double Temperature { get; set; } = 0.0;
    public int MaxOutputTokens { get; set; } = 1024;
    public bool AutoFreezeOnSwitch { get; set; } = true;
}

public class EnvironmentConfig
{
    public string Name { get; set; } = "development";
    public bool Debug { get; set; } = false;
    public string LogLevel { get; set; } = "Information";
    public string Timezone { get; set; } = "Europe/Moscow";
}

public class LlmConfig
{
    public string Provider { get; set; } = "gemini";
    public LlmProxyConfig Proxy { get; set; } = new();
    public LlmModelsConfig Models { get; set; } = new();
    public LlmParametersConfig Parameters { get; set; } = new();
    public LlmRateLimitsConfig RateLimits { get; set; } = new();
}

public class LlmProxyConfig
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string ApiKeyEnvVar { get; set; } = "GEMINI_API_KEY";
    public string OrganizationId { get; set; } = string.Empty;
}

public class LlmModelsConfig
{
    public string ChatModel { get; set; } = "gemini-1.5-flash";
    public string FastModel { get; set; } = "gemini-1.5-flash";
    public string EmbeddingModel { get; set; } = "gemini-embedding-2";
    public string FallbackModel { get; set; } = "gemini-1.5-flash";
}

public class LlmParametersConfig
{
    public double Temperature { get; set; } = 0.2;
    public double TopP { get; set; } = 0.95;
    public int MaxTokens { get; set; } = 4096;
    public double FrequencyPenalty { get; set; } = 0.1;
    public double PresencePenalty { get; set; } = 0.0;
    public int TimeoutSeconds { get; set; } = 60;
}

public class LlmRateLimitsConfig
{
    public int Rpm { get; set; } = 60;
    public int Tpm { get; set; } = 1000000;
    public int MaxConcurrentRequests { get; set; } = 5;
}

public class LoadBalancerConfig
{
    public int MinIntervalBetweenRequestsMs { get; set; } = 250;
    public int QueueCapacity { get; set; } = 1000;
    public int NginxBurstSize { get; set; } = 40;
    public int QueueTimeoutMs { get; set; } = 30000;
    public bool EnableExecutivePriority { get; set; } = true;
}

public class ResilienceConfig
{
    public int MaxRetries { get; set; } = 3;
    public int InitialDelayMs { get; set; } = 200;
    public int MaxDelayMs { get; set; } = 4000;
    public double BackoffMultiplier { get; set; } = 2.0;
    public bool EnableJitter { get; set; } = true;
    public bool AutoRollbackOnFailure { get; set; } = true;
    public CircuitBreakerConfig CircuitBreaker { get; set; } = new();
}

public class CircuitBreakerConfig
{
    public bool Enabled { get; set; } = true;
    public int FailureThreshold { get; set; } = 5;
    public int RecoveryTimeSeconds { get; set; } = 30;
}

public class RagConfig
{
    public int EmbeddingDimensions { get; set; } = 1536;
    public int TopK { get; set; } = 5;
    public double SimilarityThreshold { get; set; } = 0.70;
    public RagChunkingConfig Chunking { get; set; } = new();
    public string DefaultAccessGroup { get; set; } = "all";
}

public class RagChunkingConfig
{
    public int ChunkSize { get; set; } = 800;
    public int ChunkOverlap { get; set; } = 150;
}

public class MeetingsConfig
{
    public int PreMeetingWindowMinutes { get; set; } = 30;
    public int DefaultDeadlineDays { get; set; } = 3;
    public bool RequireApprovalBeforeBitrixExport { get; set; } = true;
    public string ExportFormat { get; set; } = "pdf";
}

public class TelemetryConfig
{
    public bool Enabled { get; set; } = true;
    public double SampleRate { get; set; } = 1.0;
    public bool AlertOnSecurityViolations { get; set; } = true;
    public bool LogRollbackEvents { get; set; } = true;
    public TelemetryFinOpsConfig FinOps { get; set; } = new();
    public TelemetryEvaluationsConfig Evaluations { get; set; } = new();
}

public class TelemetryFinOpsConfig
{
    public double PromptTokenCostPer1k { get; set; } = 0.000075;
    public double CompletionTokenCostPer1k { get; set; } = 0.000300;
    public double CachedTokenCostPer1k { get; set; } = 0.00001875;
}

public class TelemetryEvaluationsConfig
{
    public bool EnableLlmAsJudge { get; set; } = true;
    public double SampleRate { get; set; } = 1.0;
    public bool EvaluateFaithfulness { get; set; } = true;
    public bool EvaluateContextSwitchAccuracy { get; set; } = true;
    public bool EvaluateFormattingAndPolicy { get; set; } = true;
}

public class SecurityConfig
{
    public bool EnforceInternalServiceAuth { get; set; } = true;
    public string InternalServiceSecretKey { get; set; } = "fotoprint-internal-service-secret-token-2026";
    public bool AntiPortScanEnabled { get; set; } = true;
    public bool BlockKnownScannerUserAgents { get; set; } = true;
    public List<string> ScannerProbePathPatterns { get; set; } = new()
    {
        ".env", ".git", "wp-", "phpmyadmin", "actuator", "cgi-bin", "setup.cgi", "swagger", "console", "shell"
    };
    public List<string> KnownScannerUserAgents { get; set; } = new()
    {
        "nmap", "masscan", "nikto", "sqlmap", "zgrab", "shodan", "censys", "gobuster", "dirbuster"
    };
    public List<string> AllowedInternalIpRanges { get; set; } = new()
    {
        "127.0.0.1", "::1", "10.", "172.", "192.168."
    };
}

public class AgentConfig
{
    public string Mode { get; set; } = "autonomous_tool_calling";
    public int MaxIterations { get; set; } = 3;
    public string SystemPromptLanguage { get; set; } = "ru";
}

public static class FotoprintConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static FotoprintConfig Load(string? filePath = null)
    {
        var path = filePath ?? FindDefaultConfigFile();
        if (!File.Exists(path))
        {
            return new FotoprintConfig();
        }

        var yaml = File.ReadAllText(path);
        return Deserializer.Deserialize<FotoprintConfig>(yaml) ?? new FotoprintConfig();
    }

    private static string FindDefaultConfigFile()
    {
        // Поиск config.yaml в текущей папке или родительских директориях
        var current = Directory.GetCurrentDirectory();
        for (int i = 0; i < 7; i++)
        {
            var candidate = Path.Combine(current, "config.yaml");
            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return "config.yaml";
    }
}
