using System.Globalization;
using System.Text.RegularExpressions;

namespace Fotoprint.Core.Data;

public sealed record MeetingSearchFilter(
    DateOnly? TargetDate,
    bool PreferLatest,
    bool HasTopicTokens);

public static partial class MeetingSearchQueryParser
{
    private static readonly (string Stem, int Month)[] RussianMonths =
    {
        ("январ", 1),
        ("феврал", 2),
        ("март", 3),
        ("апрел", 4),
        ("мая", 5),
        ("май", 5),
        ("мае", 5),
        ("июн", 6),
        ("июл", 7),
        ("август", 8),
        ("сентябр", 9),
        ("октябр", 10),
        ("ноябр", 11),
        ("декабр", 12)
    };

    [GeneratedRegex(@"\b(\d{4})-(\d{2})-(\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})\.(\d{1,2})(?:\.(\d{2,4}))?\b", RegexOptions.Compiled)]
    private static partial Regex DotDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s+([а-яё]+)(?:\s+(\d{4}))?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianMonthDateRegex();

    [GeneratedRegex(@"(последн\w*|крайн\w*|свеж\w*|недавн\w*|last|latest)\s+(созвон\w*|встреч\w*|митинг\w*|оперативк\w*|переговор\w*|meeting\w*)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LatestMeetingRegex();

    public static MeetingSearchFilter Parse(string? query, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new MeetingSearchFilter(null, false, false);
        }

        var nowMoscow = (utcNow ?? DateTime.UtcNow).AddHours(3);
        var today = DateOnly.FromDateTime(nowMoscow);
        var lower = query.Trim().ToLowerInvariant();

        DateOnly? targetDate = null;

        if (lower.Contains("позавчера"))
        {
            targetDate = today.AddDays(-2);
        }
        else if (lower.Contains("вчера"))
        {
            targetDate = today.AddDays(-1);
        }
        else if (lower.Contains("сегодня"))
        {
            targetDate = today;
        }

        if (!targetDate.HasValue)
        {
            var isoMatch = IsoDateRegex().Match(lower);
            if (isoMatch.Success &&
                int.TryParse(isoMatch.Groups[1].Value, out var y) &&
                int.TryParse(isoMatch.Groups[2].Value, out var m) &&
                int.TryParse(isoMatch.Groups[3].Value, out var d) &&
                TryCreateDate(y, m, d, out var parsedIso))
            {
                targetDate = parsedIso;
            }
        }

        if (!targetDate.HasValue)
        {
            var dotMatch = DotDateRegex().Match(lower);
            if (dotMatch.Success &&
                int.TryParse(dotMatch.Groups[1].Value, out var d) &&
                int.TryParse(dotMatch.Groups[2].Value, out var m))
            {
                var y = today.Year;
                if (dotMatch.Groups[3].Success && int.TryParse(dotMatch.Groups[3].Value, out var parsedYear))
                {
                    y = parsedYear < 100 ? 2000 + parsedYear : parsedYear;
                }
                if (TryCreateDate(y, m, d, out var parsedDot))
                {
                    targetDate = parsedDot;
                }
            }
        }

        if (!targetDate.HasValue)
        {
            var ruMatch = RussianMonthDateRegex().Match(lower);
            if (ruMatch.Success && int.TryParse(ruMatch.Groups[1].Value, out var d))
            {
                var monthWord = ruMatch.Groups[2].Value;
                var matchedMonth = RussianMonths.FirstOrDefault(x => monthWord.StartsWith(x.Stem, StringComparison.OrdinalIgnoreCase));
                if (matchedMonth.Month > 0)
                {
                    var y = today.Year;
                    if (ruMatch.Groups[3].Success && int.TryParse(ruMatch.Groups[3].Value, out var parsedYear))
                    {
                        y = parsedYear;
                    }
                    if (TryCreateDate(y, matchedMonth.Month, d, out var parsedRu))
                    {
                        targetDate = parsedRu;
                    }
                }
            }
        }

        var preferLatest = LatestMeetingRegex().IsMatch(lower);

        // Определяем, есть ли в запросе содержательные слова помимо шаблонных слов о дате/созвоне
        var stripped = lower;
        stripped = IsoDateRegex().Replace(stripped, " ");
        stripped = DotDateRegex().Replace(stripped, " ");
        stripped = RussianMonthDateRegex().Replace(stripped, " ");
        stripped = LatestMeetingRegex().Replace(stripped, " ");
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "что", "было", "на", "в", "во", "по", "о", "об", "про", "с", "со", "и", "или",
            "как", "какой", "какая", "какие", "где", "когда", "кто", "покажи", "пришли", "найди",
            "расскажи", "говорили", "обсуждали", "решили", "созвон", "созвоне", "созвона", "созвону",
            "встреча", "встрече", "встречи", "сегодня", "сегодняшний", "сегодняшнем", "вчера",
            "вчерашний", "вчерашнем", "позавчера", "позавчерашнем", "последний", "последнем",
            "крайний", "протокол", "стенограмму", "саммари", "итоги", "docx", "pdf"
        };
        var meaningfulTokens = stripped
            .Split(new[] { ' ', ',', '.', '!', '?', ':', ';', '"', '\'', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !stopWords.Contains(t))
            .ToList();

        return new MeetingSearchFilter(targetDate, preferLatest, meaningfulTokens.Count > 0);
    }

    private static bool TryCreateDate(int year, int month, int day, out DateOnly date)
    {
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }
}

