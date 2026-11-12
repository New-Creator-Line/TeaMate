using Fotoprint.Core.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fotoprint.Core.Audio;

public interface IVocabularyProvider
{
    string Domain { get; }
    IReadOnlyList<string> GetPromptTerms();
    string GetPromptString();
    IReadOnlyDictionary<string, string> GetReplacements();
}

public class YamlVocabularyProvider : IVocabularyProvider
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ILogger<YamlVocabularyProvider>? _logger;
    private readonly VocabularyFileModel _model;

    public string Domain => _model.Domain;

    public YamlVocabularyProvider(FotoprintConfig? config = null, ILogger<YamlVocabularyProvider>? logger = null)
        : this(ResolveVocabularyPath(config?.Audio?.VocabularyPath ?? "config/vocabulary.yaml") ?? "config/vocabulary.yaml", logger)
    {
    }

    public YamlVocabularyProvider(string vocabularyFilePath, ILogger<YamlVocabularyProvider>? logger = null)
    {
        _logger = logger;
        _model = LoadVocabulary(vocabularyFilePath ?? "config/vocabulary.yaml");
    }

    public IReadOnlyList<string> GetPromptTerms()
    {
        return _model.PromptTerms ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public string GetPromptString()
    {
        var terms = GetPromptTerms();
        return terms.Count > 0 ? string.Join(", ", terms) : string.Empty;
    }

    public IReadOnlyDictionary<string, string> GetReplacements()
    {
        return _model.Replacements ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
    }

    private VocabularyFileModel LoadVocabulary(string configuredPath)
    {
        try
        {
            var resolvedPath = ResolveVocabularyPath(configuredPath);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            {
                var yaml = File.ReadAllText(resolvedPath);
                var model = Deserializer.Deserialize<VocabularyFileModel>(yaml);
                if (model != null)
                {
                    _logger?.LogInformation("[YamlVocabularyProvider] Loaded vocabulary from {Path} (Domain: {Domain}, {TermCount} terms, {ReplacementCount} replacements)",
                        resolvedPath, model.Domain, model.PromptTerms?.Count ?? 0, model.Replacements?.Count ?? 0);
                    return model;
                }
            }

            _logger?.LogWarning("[YamlVocabularyProvider] Vocabulary file not found or empty at {Path}. Using default empty vocabulary.", configuredPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[YamlVocabularyProvider] Error loading vocabulary file from {Path}.", configuredPath);
        }

        return new VocabularyFileModel();
    }

    public static string? ResolveVocabularyPath(string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            candidatePath = "config/vocabulary.yaml";
        }

        if (Path.IsPathRooted(candidatePath) && File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var searchRoots = new List<string>
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var root in searchRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            var current = root;
            for (int i = 0; i <= 6; i++)
            {
                var combined = Path.Combine(current, candidatePath);
                if (File.Exists(combined))
                {
                    return Path.GetFullPath(combined);
                }

                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
        }

        return candidatePath;
    }

    private sealed class VocabularyFileModel
    {
        public string Version { get; set; } = "1.0";
        public string Domain { get; set; } = "typography_fotoprint";
        public List<string> PromptTerms { get; set; } = new();
        public Dictionary<string, string> Replacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
