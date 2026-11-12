using System.Text.RegularExpressions;

namespace Fotoprint.Core.Security;

/// <summary>
/// Маскирование чувствительных данных (ПДн, платёжные реквизиты, зарплаты) в логах, аудите и телеметрии (AI-06).
/// Чистые функции без состояния: безопасны для горячих путей.
/// </summary>
public static class PiiMasker
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", Opts);
    private static readonly Regex Card = new(@"\b(?:\d[ \-]?){13,19}\b", Opts);
    private static readonly Regex Iban = new(@"\b[A-Z]{2}\d{2}[A-Z0-9]{4}\d{4}[A-Z0-9]{8,20}\b", Opts);
    private static readonly Regex Phone = new(@"(?<!\w)(?:\+?375|8\s?0\d{2}|\+?7)[\s\-\(\)]*\d{2,3}[\s\-\)]*\d{2,3}[\s\-]*\d{2}[\s\-]*\d{2}(?!\d)", Opts);
    private static readonly Regex Salary = new(
        @"(?i)(зарплат\w*|оклад\w*|заработн\w+\s+плат\w*|salary)\s*[:=\-—–]?\s*(?:составля\w+\s+)?\d[\d\s.,]*\s?(?:byn|br|бел\.?\s?руб\w*|руб\w*|₽|\$|usd|eur|€)",
        Opts);
    private static readonly Regex FioFull = new(
        @"\b[А-ЯЁ][а-яё]+\s+[А-ЯЁ][а-яё]+\s+[А-ЯЁ][а-яё]+(?:ович|евич|ьич|ич|овна|евна|ична|инична)\b", Opts);
    private static readonly Regex FioShort = new(@"\b[А-ЯЁ][а-яё]{2,}\s+[А-ЯЁ]\.\s?[А-ЯЁ]\.", Opts);

    /// <summary>Полное маскирование для журналов и аудита: секреты, контакты, зарплаты и ФИО.</summary>
    public static string MaskForLogs(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var s = MaskSecrets(text);
        s = FioFull.Replace(s, "[ФИО]");
        s = FioShort.Replace(s, "[ФИО]");
        return s;
    }

    /// <summary>
    /// Маскирование для исходящих запросов во внешний LLM-API: ФИО сохраняются (нужны для назначения задач),
    /// но платёжные реквизиты, контакты и суммы зарплат не покидают периметр.
    /// </summary>
    public static string MaskSecrets(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var s = Email.Replace(text, "[EMAIL]");
        s = Iban.Replace(s, "[IBAN]");
        s = Salary.Replace(s, m => $"{m.Groups[1].Value}: [СКРЫТО]");
        s = Card.Replace(s, m => LooksLikeCard(m.Value) ? "[CARD]" : m.Value);
        s = Phone.Replace(s, "[PHONE]");
        return s;
    }

    private static bool LooksLikeCard(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length is < 13 or > 19) return false;
        // Luhn: исключает ложные срабатывания на номера заказов и телефоны.
        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (alt) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            alt = !alt;
        }
        return sum % 10 == 0;
    }
}
