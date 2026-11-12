using System.Text.RegularExpressions;

namespace Fotoprint.Core.Formatting;

/// <summary>
/// Converts Markdown-formatted text into clean plain text suitable for Telegram
/// and CRM UIs that render messages without Markdown parsing.
/// </summary>
public static partial class PlainTextFormatter
{
    private static readonly Regex FencedCodeBlockRegex = new(
        @"```[^\n]*\n([\s\S]*?)```",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InlineTripleBacktickRegex = new(
        @"```([^`\r\n]+)```",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InlineCodeRegex = new(
        @"`([^`\r\n]+)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownImageRegex = new(
        @"!\[([^\[\]\r\n]*)\]\(([^)\r\n]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownLinkRegex = new(
        @"\[([^\[\]\r\n]+)\]\(([^)\r\n]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AtxHeadingRegex = new(
        @"^[ \t]*#{1,6}[ \t]+(.+?)[ \t]*#*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex BlockquoteRegex = new(
        @"^[ \t]*>[ \t]?",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex HorizontalRuleRegex = new(
        @"^[ \t]*(?:[-*_][ \t]*){3,}$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex TableSeparatorRowRegex = new(
        @"^[ \t]*\|?(?:[ \t]*:?-{2,}:?[ \t]*\|)+[ \t]*:?-{2,}:?[ \t]*\|?[ \t]*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex TableOuterPipesRegex = new(
        @"^[ \t]*\|(.+?)\|[ \t]*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex BulletAsteriskPlusRegex = new(
        @"^([ \t]*)[*+][ \t]+",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex BoldItalicAsteriskRegex = new(
        @"\*\*\*([^\*\r\n]+?)\*\*\*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BoldItalicUnderscoreRegex = new(
        @"(?<![\p{L}\p{N}_])___([^_\r\n]+?)___(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BoldAsteriskRegex = new(
        @"\*\*([^\r\n]+?)\*\*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BoldUnderscoreRegex = new(
        @"(?<![\p{L}\p{N}])__([^_\r\n]+?)__(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StrikethroughRegex = new(
        @"~~([^~\r\n]+?)~~",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ItalicAsteriskRegex = new(
        @"(?<!\*)\*(?!\s)([^\*\r\n]+?)(?<!\s)\*(?!\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ItalicUnderscoreRegex = new(
        @"(?<![\p{L}\p{N}_])_(?!\s)([^_\r\n]+?)(?<!\s)_(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EscapedMarkdownCharRegex = new(
        @"\\([*_`#])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingSpacesPerLineRegex = new(
        @"[ \t]+\n",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExcessNewlinesRegex = new(
        @"\n{3,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Strips Markdown syntax from <paramref name="text"/> and returns normalized plain text.
    /// Preserves emojis, Cyrillic text, JSON snippets, snake_case identifiers, and [Источник: ...] tags.
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var result = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // 1. Unwrap code fences and inline code before inline formatting rules run
        result = FencedCodeBlockRegex.Replace(result, m => m.Groups[1].Value.TrimEnd('\n'));
        result = InlineTripleBacktickRegex.Replace(result, "$1");
        result = InlineCodeRegex.Replace(result, "$1");

        // 2. Links and images -> readable plain text
        result = MarkdownImageRegex.Replace(result, m =>
        {
            var alt = m.Groups[1].Value.Trim();
            var url = m.Groups[2].Value.Trim();
            return string.IsNullOrEmpty(alt) ? url : $"{alt} ({url})";
        });

        result = MarkdownLinkRegex.Replace(result, m =>
        {
            var label = m.Groups[1].Value.Trim();
            var url = m.Groups[2].Value.Trim();
            if (string.IsNullOrEmpty(label)) return url;
            return label.Contains(url, StringComparison.OrdinalIgnoreCase) ? label : $"{label} ({url})";
        });

        // 3. Block-level Markdown constructs
        result = AtxHeadingRegex.Replace(result, "$1");
        result = BlockquoteRegex.Replace(result, string.Empty);
        result = TableSeparatorRowRegex.Replace(result, string.Empty);
        result = TableOuterPipesRegex.Replace(result, m =>
        {
            var cells = m.Groups[1].Value
                .Split('|')
                .Select(c => c.Trim());
            return string.Join(" | ", cells);
        });
        result = HorizontalRuleRegex.Replace(result, string.Empty);

        // Convert Markdown * or + list markers into clean Unicode bullets before italic pass
        result = BulletAsteriskPlusRegex.Replace(result, "$1• ");

        // 4. Inline emphasis and strikethrough
        result = BoldItalicAsteriskRegex.Replace(result, "$1");
        result = BoldItalicUnderscoreRegex.Replace(result, "$1");
        result = BoldAsteriskRegex.Replace(result, "$1");
        result = BoldUnderscoreRegex.Replace(result, "$1");
        result = StrikethroughRegex.Replace(result, "$1");
        result = ItalicAsteriskRegex.Replace(result, "$1");
        result = ItalicUnderscoreRegex.Replace(result, "$1");

        // 5. Unescape escaped Markdown symbols and normalize whitespace
        result = EscapedMarkdownCharRegex.Replace(result, "$1");
        result = TrailingSpacesPerLineRegex.Replace(result, "\n");
        result = ExcessNewlinesRegex.Replace(result, "\n\n");

        return result.Trim();
    }
}
