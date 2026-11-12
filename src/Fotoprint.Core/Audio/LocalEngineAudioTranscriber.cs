using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fotoprint.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Audio;

public class LocalEngineAudioTranscriber : IAudioTranscriber
{
    private static readonly Regex TimestampLineRegex = new(
        @"^\s*\[(?<start>\d{1,2}:\d{2}(?::\d{2})?[\.,]\d{1,3})\s*-->\s*(?<end>\d{1,2}:\d{2}(?::\d{2})?[\.,]\d{1,3})\]\s*(?<text>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> AllowedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".ogg", ".oga", ".opus", ".webm", ".flac", ".aac", ".mp4"
    };

    private readonly IAudioEngineResolver _engineResolver;
    private readonly IVocabularyProvider _vocabularyProvider;
    private readonly IVocabularyPostProcessor _postProcessor;
    private readonly FotoprintConfig _config;
    private readonly ILogger<LocalEngineAudioTranscriber>? _logger;

    public LocalEngineAudioTranscriber(
        IAudioEngineResolver engineResolver,
        IVocabularyProvider vocabularyProvider,
        IVocabularyPostProcessor postProcessor,
        FotoprintConfig config,
        ILogger<LocalEngineAudioTranscriber>? logger = null)
    {
        _engineResolver = engineResolver ?? throw new ArgumentNullException(nameof(engineResolver));
        _vocabularyProvider = vocabularyProvider ?? throw new ArgumentNullException(nameof(vocabularyProvider));
        _postProcessor = postProcessor ?? throw new ArgumentNullException(nameof(postProcessor));
        _config = config ?? new FotoprintConfig();
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_config.Audio.Enabled)
        {
            return Task.FromResult(false);
        }

        var enginePath = _engineResolver.ResolveEnginePath();
        var modelPath = _engineResolver.ResolveModelPath();

        var available = !string.IsNullOrWhiteSpace(enginePath) &&
                        File.Exists(enginePath) &&
                        !string.IsNullOrWhiteSpace(modelPath) &&
                        File.Exists(modelPath);

        return Task.FromResult(available);
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string language = "ru",
        CancellationToken ct = default)
    {
        if (audioStream == null)
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: _engineResolver.GetProviderName(),
                DurationMs: 0,
                ErrorMessage: "Audio stream is null.");
        }

        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "audio.wav" : Path.GetFileName(fileName);
        var ext = Path.GetExtension(safeFileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedAudioExtensions.Contains(ext))
        {
            ext = ".wav";
        }

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"fotoprint_input_{Guid.NewGuid():N}{ext}");
        try
        {
            if (audioStream.CanSeek && audioStream.Position > 0)
            {
                audioStream.Position = 0;
            }

            await using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await audioStream.CopyToAsync(fs, ct);
            }

            return await TranscribeFileAsync(tempFilePath, language, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[LocalEngineAudioTranscriber] Failed to process stream for {FileName}", fileName);
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: _engineResolver.GetProviderName(),
                DurationMs: 0,
                ErrorMessage: $"Audio stream processing error: {ex.Message}");
        }
        finally
        {
            TryDeleteFile(tempFilePath);
        }
    }

    public async Task<TranscriptionResult> TranscribeFileAsync(
        string filePath,
        string language = "ru",
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var provider = _engineResolver.GetProviderName();

        if (!_config.Audio.Enabled)
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: "disabled",
                DurationMs: 0,
                ErrorMessage: "Audio transcription is disabled in configuration.");
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: provider,
                DurationMs: sw.ElapsedMilliseconds,
                ErrorMessage: $"Audio file not found: {filePath}");
        }

        var enginePath = _engineResolver.ResolveEnginePath();
        if (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath))
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: provider,
                DurationMs: sw.ElapsedMilliseconds,
                ErrorMessage: $"C++ transcription engine binary not found. Expected under tools/engine/.");
        }

        var modelPath = _engineResolver.ResolveModelPath();
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: provider,
                DurationMs: sw.ElapsedMilliseconds,
                ErrorMessage: $"Whisper model file (.bin) not found. Run ./scripts/download-model.sh.");
        }

        var lang = string.IsNullOrWhiteSpace(language) ? "ru" : language.Trim();
        var timeoutSeconds = _config.Audio.Engine.TimeoutSeconds > 0
            ? _config.Audio.Engine.TimeoutSeconds
            : 1800;

        var tempWavPath = Path.Combine(Path.GetTempPath(), $"fotoprint_pcm_{Guid.NewGuid():N}.wav");
        var tempOutPrefix = Path.Combine(Path.GetTempPath(), $"fotoprint_out_{Guid.NewGuid():N}");
        var tempOutJsonPath = tempOutPrefix + ".json";

        try
        {
            // Convert to 16kHz mono PCM WAV if converter is available
            var wavInputPath = await ConvertTo16kMonoWavAsync(filePath, tempWavPath, ct);

            // Vocabulary prompt from IVocabularyProvider
            var vocabularyPrompt = _vocabularyProvider.GetPromptString();

            var psi = new ProcessStartInfo
            {
                FileName = enginePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add(modelPath);
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add(lang);
            psi.ArgumentList.Add("-np");

            if (!string.IsNullOrWhiteSpace(vocabularyPrompt))
            {
                psi.ArgumentList.Add("--prompt");
                psi.ArgumentList.Add(vocabularyPrompt);
            }

            psi.ArgumentList.Add("-oj");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add(tempOutPrefix);
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(wavInputPath);

            var (exitCode, stdout, stderr) = await ExecuteProcessAsync(psi, TimeSpan.FromSeconds(timeoutSeconds), ct);

            TranscriptionResult rawResult;
            if (File.Exists(tempOutJsonPath))
            {
                var jsonText = await File.ReadAllTextAsync(tempOutJsonPath, ct);
                rawResult = ParseEngineOutput(jsonText, sw.ElapsedMilliseconds, provider);
            }
            else if (!string.IsNullOrWhiteSpace(stdout))
            {
                rawResult = ParseEngineOutput(stdout, sw.ElapsedMilliseconds, provider);
            }
            else
            {
                var errMsg = !string.IsNullOrWhiteSpace(stderr)
                    ? stderr.Trim()
                    : $"Engine exited with code {exitCode} and produced no output.";
                _logger?.LogWarning("[LocalEngineAudioTranscriber] Engine failed: {Error}", errMsg);
                return new TranscriptionResult(
                    Success: false,
                    Text: string.Empty,
                    Segments: new List<AudioSegment>(),
                    Provider: provider,
                    DurationMs: sw.ElapsedMilliseconds,
                    ErrorMessage: errMsg);
            }

            if (!rawResult.Success && !string.IsNullOrWhiteSpace(stderr) && string.IsNullOrWhiteSpace(rawResult.ErrorMessage))
            {
                rawResult = rawResult with { ErrorMessage = stderr.Trim() };
            }

            // Apply IVocabularyPostProcessor for homophone correction
            var finalResult = _postProcessor.ProcessResult(rawResult);
            return finalResult;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[LocalEngineAudioTranscriber] Execution error for {FilePath}", filePath);
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: provider,
                DurationMs: sw.ElapsedMilliseconds,
                ErrorMessage: $"Local engine exception: {ex.Message}");
        }
        finally
        {
            TryDeleteFile(tempWavPath);
            TryDeleteFile(tempOutJsonPath);
            TryDeleteFile(tempOutPrefix + ".txt");
        }
    }

    private async Task<string> ConvertTo16kMonoWavAsync(string inputPath, string targetWavPath, CancellationToken ct)
    {
        try
        {
            var converterPath = _engineResolver.ResolveAudioConverterPath();
            if (string.IsNullOrWhiteSpace(converterPath) || !File.Exists(converterPath))
            {
                return inputPath;
            }

            ProcessStartInfo psi;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                converterPath.EndsWith("afconvert", StringComparison.OrdinalIgnoreCase))
            {
                psi = new ProcessStartInfo
                {
                    FileName = converterPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("WAVE");
                psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add("LEI16@16000");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add(targetWavPath);
            }
            else
            {
                // ffmpeg
                psi = new ProcessStartInfo
                {
                    FileName = converterPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add("-ar");
                psi.ArgumentList.Add("16000");
                psi.ArgumentList.Add("-ac");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("pcm_s16le");
                psi.ArgumentList.Add(targetWavPath);
            }

            var (exitCode, _, stderr) = await ExecuteProcessAsync(psi, TimeSpan.FromSeconds(300), ct);
            if (exitCode == 0 && File.Exists(targetWavPath))
            {
                return targetWavPath;
            }

            _logger?.LogWarning("[LocalEngineAudioTranscriber] Audio converter '{Converter}' exited with {ExitCode}: {Error}. Target exists: {Exists}", converterPath, exitCode, stderr.Trim(), File.Exists(targetWavPath));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[LocalEngineAudioTranscriber] Audio conversion exception for input '{InputPath}'", inputPath);
        }

        return inputPath;
    }

    public static TranscriptionResult ParseEngineOutput(
        string rawOutput,
        long durationMs,
        string provider = "local-metal")
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: provider,
                DurationMs: durationMs,
                ErrorMessage: "Empty output from transcription engine.");
        }

        var trimmed = rawOutput.Trim();

        // 1. Try JSON parsing
        var jsonCandidate = ExtractJsonObject(trimmed);
        if (!string.IsNullOrWhiteSpace(jsonCandidate))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonCandidate);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetPropertyCaseInsensitive(root, "error", out var errProp) &&
                        errProp.ValueKind == JsonValueKind.String)
                    {
                        var errText = errProp.GetString();
                        if (!string.IsNullOrWhiteSpace(errText) &&
                            (!TryGetPropertyCaseInsensitive(root, "text", out var txtCheck) ||
                             string.IsNullOrWhiteSpace(txtCheck.GetString())))
                        {
                            return new TranscriptionResult(
                                Success: false,
                                Text: string.Empty,
                                Segments: new List<AudioSegment>(),
                                Provider: provider,
                                DurationMs: durationMs,
                                ErrorMessage: errText);
                        }
                    }

                    // Format A: whisper.cpp / tsukiko-recognizer JSON with "transcription" array
                    if (TryGetPropertyCaseInsensitive(root, "transcription", out var transArray) &&
                        transArray.ValueKind == JsonValueKind.Array)
                    {
                        var whisperSegments = new List<AudioSegment>();
                        var textParts = new List<string>();

                        foreach (var item in transArray.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;

                            var rawSegText = TryGetPropertyCaseInsensitive(item, "text", out var tProp) && tProp.ValueKind == JsonValueKind.String
                                ? (tProp.GetString()?.Trim() ?? string.Empty)
                                : string.Empty;

                            if (string.IsNullOrWhiteSpace(rawSegText)) continue;

                            var segText = DeduplicateRepeatedPhrases(rawSegText);
                            if (string.IsNullOrWhiteSpace(segText)) continue;

                            // Consecutive duplicate segment filtering
                            if (whisperSegments.Count > 0 && IsDuplicateSegmentText(segText, whisperSegments[^1].Text))
                            {
                                continue;
                            }

                            double startSec = 0;
                            double endSec = 0;

                            if (TryGetPropertyCaseInsensitive(item, "offsets", out var offsets) &&
                                offsets.ValueKind == JsonValueKind.Object)
                            {
                                if (TryGetPropertyCaseInsensitive(offsets, "from", out var fromMs) && fromMs.TryGetDouble(out var fVal))
                                {
                                    startSec = Math.Round(fVal / 1000.0, 3);
                                }
                                if (TryGetPropertyCaseInsensitive(offsets, "to", out var toMs) && toMs.TryGetDouble(out var tVal))
                                {
                                    endSec = Math.Round(tVal / 1000.0, 3);
                                }
                            }
                            else if (TryGetPropertyCaseInsensitive(item, "timestamps", out var timestamps) &&
                                     timestamps.ValueKind == JsonValueKind.Object)
                            {
                                if (TryGetPropertyCaseInsensitive(timestamps, "from", out var fromTs) && fromTs.ValueKind == JsonValueKind.String)
                                {
                                    TryParseTimestampToSeconds(fromTs.GetString(), out startSec);
                                }
                                if (TryGetPropertyCaseInsensitive(timestamps, "to", out var toTs) && toTs.ValueKind == JsonValueKind.String)
                                {
                                    TryParseTimestampToSeconds(toTs.GetString(), out endSec);
                                }
                            }

                            string? speaker = null;
                            if (TryGetPropertyCaseInsensitive(item, "speaker", out var spkProp) && spkProp.ValueKind == JsonValueKind.String)
                            {
                                speaker = spkProp.GetString();
                            }

                            whisperSegments.Add(new AudioSegment(startSec, endSec, segText, speaker));
                            textParts.Add(segText);
                        }

                        var combinedWhisperText = string.Join(" ", textParts).Trim();
                        combinedWhisperText = DeduplicateRepeatedPhrases(combinedWhisperText);
                        return new TranscriptionResult(
                            Success: !string.IsNullOrWhiteSpace(combinedWhisperText),
                            Text: combinedWhisperText,
                            Segments: whisperSegments,
                            Provider: provider,
                            DurationMs: durationMs,
                            ErrorMessage: string.IsNullOrWhiteSpace(combinedWhisperText) ? "No speech detected in audio." : null);
                    }

                    // Format B: Standard JSON with "text" and optional "segments"
                    var hasTextProp = TryGetPropertyCaseInsensitive(root, "text", out var rootTextProp);
                    var hasSegmentsProp = TryGetPropertyCaseInsensitive(root, "segments", out var rootSegmentsProp);

                    if (hasTextProp || hasSegmentsProp)
                    {
                        var segments = new List<AudioSegment>();
                        if (hasSegmentsProp && rootSegmentsProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var seg in rootSegmentsProp.EnumerateArray())
                            {
                                if (seg.ValueKind != JsonValueKind.Object) continue;

                                var rawSegText = TryGetPropertyCaseInsensitive(seg, "text", out var stProp) && stProp.ValueKind == JsonValueKind.String
                                    ? (stProp.GetString()?.Trim() ?? string.Empty)
                                    : string.Empty;

                                if (string.IsNullOrWhiteSpace(rawSegText)) continue;

                                var segText = DeduplicateRepeatedPhrases(rawSegText);
                                if (string.IsNullOrWhiteSpace(segText)) continue;

                                // Consecutive duplicate segment filtering
                                if (segments.Count > 0 && IsDuplicateSegmentText(segText, segments[^1].Text))
                                {
                                    continue;
                                }

                                double startSec = ExtractSeconds(seg, "start", "start_seconds", "startSeconds", "startMs");
                                double endSec = ExtractSeconds(seg, "end", "end_seconds", "endSeconds", "endMs");

                                string? speaker = null;
                                if (TryGetPropertyCaseInsensitive(seg, "speaker", out var spkProp) && spkProp.ValueKind == JsonValueKind.String)
                                {
                                    speaker = spkProp.GetString();
                                }

                                segments.Add(new AudioSegment(startSec, endSec, segText, speaker));
                            }
                        }

                        var rootText = hasTextProp && rootTextProp.ValueKind == JsonValueKind.String
                            ? (rootTextProp.GetString()?.Trim() ?? string.Empty)
                            : string.Empty;

                        if (!string.IsNullOrWhiteSpace(rootText))
                        {
                            rootText = DeduplicateRepeatedPhrases(rootText);
                        }

                        if (string.IsNullOrWhiteSpace(rootText) && segments.Count > 0)
                        {
                            rootText = string.Join(" ", segments.Select(s => s.Text)).Trim();
                            rootText = DeduplicateRepeatedPhrases(rootText);
                        }

                        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(rootText))
                        {
                            segments.Add(new AudioSegment(0.0, Math.Max(0.0, Math.Round(durationMs / 1000.0, 2)), rootText));
                        }

                        return new TranscriptionResult(
                            Success: !string.IsNullOrWhiteSpace(rootText),
                            Text: rootText,
                            Segments: segments,
                            Provider: provider,
                            DurationMs: durationMs,
                            ErrorMessage: string.IsNullOrWhiteSpace(rootText) ? "Empty text in JSON output." : null);
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to timestamped / plain text
            }
        }

        // 2. Timestamped lines: [00:00:00.000 --> 00:00:03.000] Text
        var matches = TimestampLineRegex.Matches(trimmed);
        if (matches.Count > 0)
        {
            var lineSegments = new List<AudioSegment>();
            var lineTexts = new List<string>();

            foreach (Match match in matches)
            {
                var startStr = match.Groups["start"].Value;
                var endStr = match.Groups["end"].Value;
                var rawLineText = match.Groups["text"].Value.Trim();

                if (string.IsNullOrWhiteSpace(rawLineText)) continue;

                var lineText = DeduplicateRepeatedPhrases(rawLineText);
                if (string.IsNullOrWhiteSpace(lineText)) continue;

                // Consecutive duplicate segment filtering
                if (lineSegments.Count > 0 && IsDuplicateSegmentText(lineText, lineSegments[^1].Text))
                {
                    continue;
                }

                TryParseTimestampToSeconds(startStr, out var startSec);
                TryParseTimestampToSeconds(endStr, out var endSec);

                lineSegments.Add(new AudioSegment(startSec, endSec, lineText));
                lineTexts.Add(lineText);
            }

            var joinedText = string.Join(" ", lineTexts).Trim();
            joinedText = DeduplicateRepeatedPhrases(joinedText);
            if (!string.IsNullOrWhiteSpace(joinedText))
            {
                return new TranscriptionResult(
                    Success: true,
                    Text: joinedText,
                    Segments: lineSegments,
                    Provider: provider,
                    DurationMs: durationMs);
            }
        }

        // 3. Plain text fallback
        var cleanedFallback = DeduplicateRepeatedPhrases(trimmed);
        return new TranscriptionResult(
            Success: true,
            Text: cleanedFallback,
            Segments: new List<AudioSegment>
            {
                new(0.0, Math.Max(0.0, Math.Round(durationMs / 1000.0, 2)), cleanedFallback)
            },
            Provider: provider,
            DurationMs: durationMs);
    }

    public static string DeduplicateRepeatedPhrases(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = input.Trim();

        // Pass 1: Deduplicate consecutive identical sentences/clauses
        text = DeduplicateConsecutiveSentences(text);

        // Pass 2: Deduplicate multi-word and repeated phrase loops
        text = DeduplicateWordNGrams(text);

        return Regex.Replace(text, @"[,;\s]+$", "").Trim();
    }

    public static bool IsDuplicateSegmentText(string? current, string? previous)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(previous))
        {
            return false;
        }

        if (string.Equals(current.Trim(), previous.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normCur = NormalizeSentence(current);
        var normPrev = NormalizeSentence(previous);

        return !string.IsNullOrEmpty(normCur) && string.Equals(normCur, normPrev, StringComparison.OrdinalIgnoreCase);
    }

    private static string DeduplicateConsecutiveSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Split sentences on sentence-ending punctuation followed by whitespace, or newline
        // but avoid splitting on decimal numbers like 1.5 or 3.14
        var rawSentences = Regex.Split(text, @"(?<=[.!?;\n\r])(?<!\d\.\d)\s+");
        if (rawSentences.Length <= 1)
        {
            return text;
        }

        var kept = new List<string>();
        string? lastNormalized = null;

        foreach (var raw in rawSentences)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var norm = NormalizeSentence(raw);
            if (!string.IsNullOrEmpty(norm) && string.Equals(norm, lastNormalized, StringComparison.OrdinalIgnoreCase))
            {
                // Consecutive duplicate sentence - skip
                continue;
            }

            if (!string.IsNullOrEmpty(norm))
            {
                lastNormalized = norm;
            }

            kept.Add(raw.Trim());
        }

        return string.Join(" ", kept).Trim();
    }

    private static string DeduplicateWordNGrams(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var current = text;
        for (var pass = 0; pass < 5; pass++)
        {
            var wordMatches = Regex.Matches(current, @"\S+");
            if (wordMatches.Count < 2)
            {
                break;
            }

            var words = new Match[wordMatches.Count];
            var normWords = new string[wordMatches.Count];
            for (var idx = 0; idx < wordMatches.Count; idx++)
            {
                words[idx] = wordMatches[idx];
                normWords[idx] = CleanWordForComparison(wordMatches[idx].Value);
            }

            var changed = false;
            var maxN = Math.Min(12, wordMatches.Count / 2);

            for (var n = maxN; n >= 1; n--)
            {
                for (var i = 0; i <= wordMatches.Count - 2 * n; i++)
                {
                    // Check if phrase1 matches phrase2
                    var matchesPhrase = true;
                    for (var k = 0; k < n; k++)
                    {
                        if (string.IsNullOrEmpty(normWords[i + k]) ||
                            !string.Equals(normWords[i + k], normWords[i + n + k], StringComparison.OrdinalIgnoreCase))
                        {
                            matchesPhrase = false;
                            break;
                        }
                    }

                    if (!matchesPhrase) continue;

                    // Determine how many times it repeats consecutively
                    var repeatCount = 2;
                    while (i + (repeatCount + 1) * n <= wordMatches.Count)
                    {
                        var nextMatch = true;
                        for (var k = 0; k < n; k++)
                        {
                            if (!string.Equals(normWords[i + k], normWords[i + repeatCount * n + k], StringComparison.OrdinalIgnoreCase))
                            {
                                nextMatch = false;
                                break;
                            }
                        }
                        if (nextMatch) repeatCount++;
                        else break;
                    }

                    // For single words (n == 1), only deduplicate if repeatCount >= 3 or word is longer than 3 characters or exact match
                    if (n == 1)
                    {
                        var raw1 = words[i].Value;
                        var raw2 = words[i + 1].Value;
                        var isExact = string.Equals(raw1, raw2, StringComparison.OrdinalIgnoreCase);
                        if (repeatCount < 3 && !isExact && normWords[i].Length <= 3)
                        {
                            continue;
                        }
                    }

                    // Remove repetitions: from words[i + n].Index to words[i + repeatCount * n - 1].Index + Length
                    var startRemove = words[i + n].Index;
                    var endRemove = words[i + repeatCount * n - 1].Index + words[i + repeatCount * n - 1].Length;

                    // Preserve trailing sentence-ending punctuation from duplicate phrase if kept phrase ended with comma or nothing
                    var lastDupWord = words[i + repeatCount * n - 1].Value;
                    var trailingPunctMatch = Regex.Match(lastDupWord, @"[.!?]+$");
                    if (trailingPunctMatch.Success)
                    {
                        var keptLastWordVal = words[i + n - 1].Value;
                        if (!Regex.IsMatch(keptLastWordVal, @"[.!?]+$"))
                        {
                            while (keptLastWordVal.Length > 0 && (keptLastWordVal.EndsWith(',') || keptLastWordVal.EndsWith(';')))
                            {
                                var keptEnd = words[i + n - 1].Index + keptLastWordVal.Length;
                                current = current.Remove(keptEnd - 1, 1);
                                keptLastWordVal = keptLastWordVal.Substring(0, keptLastWordVal.Length - 1);
                                startRemove--;
                                endRemove--;
                            }

                            var insertPos = words[i + n - 1].Index + keptLastWordVal.Length;
                            current = current.Insert(insertPos, trailingPunctMatch.Value);
                            startRemove += trailingPunctMatch.Length;
                            endRemove += trailingPunctMatch.Length;
                        }
                    }
                    else
                    {
                        // Duplicate did not end with terminal punctuation [.!?]
                        // If duplicate was followed by end of string, dangling separator, or sentence boundary,
                        // strip trailing separator punctuation (commas, semicolons) from the kept phrase
                        var textAfter = current.Substring(endRemove);
                        var isEndOrSentenceBoundary = string.IsNullOrWhiteSpace(textAfter) ||
                                                      Regex.IsMatch(textAfter, @"^[,;\s]*$") ||
                                                      Regex.IsMatch(textAfter, @"^\s*[.!?\n\r]");
                        if (isEndOrSentenceBoundary)
                        {
                            var keptLastWordVal = words[i + n - 1].Value;
                            while (keptLastWordVal.Length > 0 && (keptLastWordVal.EndsWith(',') || keptLastWordVal.EndsWith(';')))
                            {
                                var keptEnd = words[i + n - 1].Index + keptLastWordVal.Length;
                                current = current.Remove(keptEnd - 1, 1);
                                keptLastWordVal = keptLastWordVal.Substring(0, keptLastWordVal.Length - 1);
                                startRemove--;
                                endRemove--;
                            }
                        }
                    }

                    // Expand startRemove backwards to consume leading space if present
                    while (startRemove > 0 && char.IsWhiteSpace(current[startRemove - 1]))
                    {
                        startRemove--;
                    }

                    current = current.Remove(startRemove, endRemove - startRemove);
                    changed = true;
                    break;
                }

                if (changed) break;
            }

            if (!changed) break;
        }

        var result = Regex.Replace(current.Trim(), @"\s+", " ");
        return Regex.Replace(result, @"[,;\s]+$", "").Trim();
    }

    private static string CleanWordForComparison(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return string.Empty;
        var cleaned = Regex.Replace(word.Trim(), @"^[^\p{L}\p{N}]+|[^\p{L}\p{N}]+$", "");
        return cleaned.ToLowerInvariant();
    }

    private static string NormalizeSentence(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var trimmed = s.Trim().TrimEnd('.', '!', '?', ';', ',', ':').Trim();
        return Regex.Replace(trimmed, @"\s+", " ").ToLowerInvariant();
    }

    private static string? ExtractJsonObject(string input)
    {
        var firstBrace = input.IndexOf('{');
        var lastBrace = input.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return input.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return null;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static double ExtractSeconds(JsonElement obj, string secKey1, string secKey2, string secKey3, string msKey)
    {
        if (TryGetPropertyCaseInsensitive(obj, secKey1, out var p1) && TryReadDouble(p1, out var v1)) return v1;
        if (TryGetPropertyCaseInsensitive(obj, secKey2, out var p2) && TryReadDouble(p2, out var v2)) return v2;
        if (TryGetPropertyCaseInsensitive(obj, secKey3, out var p3) && TryReadDouble(p3, out var v3)) return v3;
        if (TryGetPropertyCaseInsensitive(obj, msKey, out var pMs) && TryReadDouble(pMs, out var vMs)) return Math.Round(vMs / 1000.0, 3);
        return 0.0;
    }

    private static bool TryReadDouble(JsonElement el, out double val)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out val))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out val))
        {
            return true;
        }

        val = 0.0;
        return false;
    }

    private static bool TryParseTimestampToSeconds(string? rawTimestamp, out double seconds)
    {
        seconds = 0.0;
        if (string.IsNullOrWhiteSpace(rawTimestamp))
        {
            return false;
        }

        var normalized = rawTimestamp.Trim().Replace(',', '.');
        var parts = normalized.Split(':');

        try
        {
            if (parts.Length == 3)
            {
                var h = double.Parse(parts[0], CultureInfo.InvariantCulture);
                var m = double.Parse(parts[1], CultureInfo.InvariantCulture);
                var s = double.Parse(parts[2], CultureInfo.InvariantCulture);
                seconds = Math.Round(h * 3600 + m * 60 + s, 3);
                return true;
            }

            if (parts.Length == 2)
            {
                var m = double.Parse(parts[0], CultureInfo.InvariantCulture);
                var s = double.Parse(parts[1], CultureInfo.InvariantCulture);
                seconds = Math.Round(m * 60 + s, 3);
                return true;
            }

            if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sOnly))
            {
                seconds = Math.Round(sOnly, 3);
                return true;
            }
        }
        catch
        {
            // Ignore format errors
        }

        return false;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> ExecuteProcessAsync(
        ProcessStartInfo psi,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderrBuilder.AppendLine(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {psi.FileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }

            throw new TimeoutException($"Process '{psi.FileName}' timed out after {timeout.TotalSeconds}s.");
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
