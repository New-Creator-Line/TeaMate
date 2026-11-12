namespace Fotoprint.Core.Audio;

public interface IAudioTranscriber
{
    Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string language = "ru",
        CancellationToken ct = default);

    Task<TranscriptionResult> TranscribeFileAsync(
        string filePath,
        string language = "ru",
        CancellationToken ct = default);

    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}
