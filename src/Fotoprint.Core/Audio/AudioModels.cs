namespace Fotoprint.Core.Audio;

public record AudioSegment(
    double StartSeconds,
    double EndSeconds,
    string Text,
    string? Speaker = null
);

public record TranscriptionResult(
    bool Success,
    string Text,
    List<AudioSegment>? Segments,
    string Provider,
    long DurationMs,
    string? ErrorMessage = null
);

public record ExtractedActionItem(
    string Title,
    string? Description,
    string Responsible,
    string DeadlineText,
    DateTime? ParsedDeadline,
    string? SourceQuote = null
);

public record MeetingAnalysisResult(
    string Title,
    string Summary,
    List<string> Decisions,
    List<string> Discussed,
    List<string> Risks,
    List<string> RequiresClarification,
    List<ExtractedActionItem> ActionItems,
    List<string>? Proposed = null
);
