using System.Text.RegularExpressions;

namespace Fotoprint.Core.Audio;

public interface IVocabularyPostProcessor
{
    string ProcessText(string? rawText);
    TranscriptionResult ProcessResult(TranscriptionResult result);
}

public class VocabularyPostProcessor : IVocabularyPostProcessor
{
    private readonly IVocabularyProvider _vocabularyProvider;

    public VocabularyPostProcessor(IVocabularyProvider vocabularyProvider)
    {
        _vocabularyProvider = vocabularyProvider ?? throw new ArgumentNullException(nameof(vocabularyProvider));
    }

    public string ProcessText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return rawText ?? string.Empty;
        }

        var replacements = _vocabularyProvider.GetReplacements();
        if (replacements == null || replacements.Count == 0)
        {
            return rawText;
        }

        var text = rawText;

        // Sort keys by descending length so that longer multi-word phrases match before shorter subphrases
        var orderedPairs = replacements
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
            .OrderByDescending(kvp => kvp.Key.Length)
            .ToList();

        foreach (var (key, value) in orderedPairs)
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(key)}(?![\p{{L}}\p{{N}}])";
            text = Regex.Replace(text, pattern, value ?? string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return text;
    }

    public TranscriptionResult ProcessResult(TranscriptionResult result)
    {
        if (result == null)
        {
            return result!;
        }

        var processedText = ProcessText(result.Text);

        List<AudioSegment>? processedSegments = null;
        if (result.Segments != null)
        {
            processedSegments = new List<AudioSegment>(result.Segments.Count);
            foreach (var segment in result.Segments)
            {
                if (segment == null) continue;
                processedSegments.Add(segment with
                {
                    Text = ProcessText(segment.Text)
                });
            }
        }

        return result with
        {
            Text = processedText,
            Segments = processedSegments
        };
    }
}
