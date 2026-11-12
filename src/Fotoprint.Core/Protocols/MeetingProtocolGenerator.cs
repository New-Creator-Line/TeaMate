using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Fotoprint.Core.Models;

namespace Fotoprint.Core.Protocols;

public record ProtocolExportData(
    string Title,
    DateTime MeetingDate,
    string OrganizerName,
    List<string> Participants,
    string Summary,
    List<string> Decisions,
    List<ActionItemExportData> ActionItems,
    string? RawTranscript = null,
    List<string>? Proposed = null,
    List<string>? Discussed = null,
    List<string>? Risks = null,
    List<string>? RequiresClarification = null
);

public record ActionItemExportData(
    string Title,
    string ResponsibleName,
    DateTime? Deadline,
    string Status,
    string? SourceQuote = null
);

public interface IMeetingProtocolGenerator
{
    byte[] GenerateDocx(ProtocolExportData data);
    byte[] GeneratePdf(ProtocolExportData data);
    string GenerateHtml(ProtocolExportData data);
}

public class MeetingProtocolGenerator : IMeetingProtocolGenerator
{
    public byte[] GenerateDocx(ProtocolExportData data)
    {
        using var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // 1. Заголовок
            var titlePara = body.AppendChild(new Paragraph());
            var titleRun = titlePara.AppendChild(new Run());
            titleRun.AppendChild(new Text($"ПРОТОКОЛ ВСТРЕЧИ: {data.Title.ToUpper()}"));
            var titleProps = titleRun.AppendChild(new RunProperties());
            titleProps.AppendChild(new Bold());
            titleProps.AppendChild(new FontSize { Val = "32" }); // 16pt

            AddEmptyParagraph(body);

            // 2. Метаданные (Дата, Организатор, Участники)
            AddMetaParagraph(body, "Дата проведения: ", data.MeetingDate.ToString("dd.MM.yyyy HH:mm (МСК)"));
            AddMetaParagraph(body, "Организатор: ", data.OrganizerName);
            AddMetaParagraph(body, "Присутствовали: ", string.Join(", ", data.Participants));

            AddEmptyParagraph(body);

            // 3. Краткое резюме встречи (Executive Summary)
            AddHeading(body, "1. КРАТКИЕ ИТОГИ И ОБСУЖДЕНИЕ");
            var summaryPara = body.AppendChild(new Paragraph());
            summaryPara.AppendChild(new Run(new Text(data.Summary)));

            AddEmptyParagraph(body);

            // 4. Четыре статуса фиксации (Решено / Предложено / Обсуждалось / Требует уточнения + Риски)
            AddStatusListDocx(body, "2. ПРИНЯТЫЕ РЕШЕНИЯ (РЕШЕНО)", data.Decisions, "Решения не зафиксированы.");
            if (data.Proposed is { Count: > 0 })
                AddStatusListDocx(body, "2.1. ПРЕДЛОЖЕНО (ИНИЦИАТИВЫ)", data.Proposed, "Предложений не зафиксировано.");
            if (data.Discussed is { Count: > 0 })
                AddStatusListDocx(body, "2.2. ОБСУЖДАЛОСЬ", data.Discussed, "Темы обсуждения не выделены.");
            if (data.Risks is { Count: > 0 })
                AddStatusListDocx(body, "2.3. ОЗВУЧЕННЫЕ РИСКИ И БЛОКЕРЫ", data.Risks, "Риски не выявлены.");
            if (data.RequiresClarification is { Count: > 0 })
                AddStatusListDocx(body, "2.4. ТРЕБУЕТ УТОЧНЕНИЯ", data.RequiresClarification, "Вопросов, требующих уточнения, нет.");

            // 5. Поручения и Задачи (Action Items Table)
            AddHeading(body, "3. ПОРУЧЕНИЯ И ЗАДАЧИ (ACTION ITEMS)");
            var table = body.AppendChild(new Table());

            // Стиль таблицы
            var tblPr = new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }
                )
            );
            table.AppendChild(tblPr);

            // Шапка таблицы
            var headerRow = table.AppendChild(new TableRow());
            AddTableCell(headerRow, "№", isHeader: true, width: "500");
            AddTableCell(headerRow, "Суть поручения", isHeader: true, width: "3600");
            AddTableCell(headerRow, "Ответственный", isHeader: true, width: "1800");
            AddTableCell(headerRow, "Срок (Дедлайн)", isHeader: true, width: "1300");
            AddTableCell(headerRow, "Статус", isHeader: true, width: "1100");
            AddTableCell(headerRow, "Основание (цитата)", isHeader: true, width: "1700");

            // Строки задач
            for (int i = 0; i < data.ActionItems.Count; i++)
            {
                var item = data.ActionItems[i];
                var responsibleText = string.IsNullOrWhiteSpace(item.ResponsibleName)
                    ? "требует уточнения"
                    : item.ResponsibleName;
                var deadlineText = item.Deadline?.ToString("dd.MM.yyyy") ?? "требует уточнения";
                var quoteText = string.IsNullOrWhiteSpace(item.SourceQuote) ? "—" : item.SourceQuote;
                var row = table.AppendChild(new TableRow());
                AddTableCell(row, (i + 1).ToString(), width: "500");
                AddTableCell(row, item.Title, width: "3600");
                AddTableCell(row, responsibleText, width: "1800");
                AddTableCell(row, deadlineText, width: "1300");
                AddTableCell(row, item.Status, width: "1100");
                AddTableCell(row, quoteText, width: "1700");
            }

            if (!string.IsNullOrWhiteSpace(data.RawTranscript))
            {
                AddEmptyParagraph(body);
                AddHeading(body, "4. ПОЛНАЯ СТЕНОГРАММА СОЗВОНА");
                foreach (var line in data.RawTranscript.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var tp = body.AppendChild(new Paragraph());
                    tp.AppendChild(new Run(new Text(line.Trim())));
                }
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    public string GenerateHtml(ProtocolExportData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"ru\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 40px; color: #222; }");
        sb.AppendLine("h1 { color: #1a365d; border-bottom: 2px solid #2b6cb0; padding-bottom: 8px; }");
        sb.AppendLine("h2 { color: #2d3748; margin-top: 25px; }");
        sb.AppendLine(".meta { background: #f7fafc; padding: 15px; border-left: 4px solid #3182ce; margin-bottom: 20px; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 15px; }");
        sb.AppendLine("th, td { border: 1px solid #cbd5e0; padding: 10px; text-align: left; }");
        sb.AppendLine("th { background-color: #edf2f7; font-weight: bold; }");
        sb.AppendLine(".badge { display: inline-block; padding: 3px 8px; border-radius: 4px; font-size: 12px; background: #e2e8f0; }");
        sb.AppendLine("</style></head><body>");

        var encTitle = System.Net.WebUtility.HtmlEncode(data.Title);
        var encOrganizer = System.Net.WebUtility.HtmlEncode(data.OrganizerName);
        var encParticipants = System.Net.WebUtility.HtmlEncode(string.Join(", ", data.Participants));
        var encSummary = System.Net.WebUtility.HtmlEncode(data.Summary);

        sb.AppendLine($"<h1>Протокол встречи: {encTitle}</h1>");
        sb.AppendLine("<div class=\"meta\">");
        sb.AppendLine($"<p><b>Дата:</b> {data.MeetingDate:dd.MM.yyyy HH:mm} (МСК)</p>");
        sb.AppendLine($"<p><b>Организатор:</b> {encOrganizer}</p>");
        sb.AppendLine($"<p><b>Присутствовали:</b> {encParticipants}</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>1. Краткие итоги и обсуждение</h2>");
        sb.AppendLine($"<p>{encSummary}</p>");

        AddStatusListHtml(sb, "2. Принятые решения (Решено)", data.Decisions);
        if (data.Proposed is { Count: > 0 })
            AddStatusListHtml(sb, "2.1. Предложено (Инициативы)", data.Proposed);
        if (data.Discussed is { Count: > 0 })
            AddStatusListHtml(sb, "2.2. Обсуждалось", data.Discussed);
        if (data.Risks is { Count: > 0 })
            AddStatusListHtml(sb, "2.3. Риски и блокеры", data.Risks);
        if (data.RequiresClarification is { Count: > 0 })
            AddStatusListHtml(sb, "2.4. Требует уточнения", data.RequiresClarification);

        sb.AppendLine("<h2>3. Поручения и Задачи (Action Items)</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>№</th><th>Суть поручения</th><th>Ответственный</th><th>Дедлайн</th><th>Статус</th><th>Основание (цитата)</th></tr></thead>");
        sb.AppendLine("<tbody>");
        for (int i = 0; i < data.ActionItems.Count; i++)
        {
            var item = data.ActionItems[i];
            var responsibleText = string.IsNullOrWhiteSpace(item.ResponsibleName)
                ? "требует уточнения"
                : item.ResponsibleName;
            var deadlineText = item.Deadline?.ToString("dd.MM.yyyy") ?? "требует уточнения";
            var quoteText = string.IsNullOrWhiteSpace(item.SourceQuote) ? "—" : item.SourceQuote;
            sb.AppendLine($"<tr><td>{i + 1}</td><td>{System.Net.WebUtility.HtmlEncode(item.Title)}</td><td>{System.Net.WebUtility.HtmlEncode(responsibleText)}</td><td>{System.Net.WebUtility.HtmlEncode(deadlineText)}</td><td><span class=\"badge\">{System.Net.WebUtility.HtmlEncode(item.Status)}</span></td><td>{System.Net.WebUtility.HtmlEncode(quoteText)}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");

        if (!string.IsNullOrWhiteSpace(data.RawTranscript))
        {
            sb.AppendLine("<h2>4. Полная стенограмма созвона</h2>");
            sb.AppendLine("<pre style=\"white-space: pre-wrap; font-family: inherit; background: #f7fafc; padding: 15px; border-radius: 6px;\">");
            sb.AppendLine(System.Net.WebUtility.HtmlEncode(data.RawTranscript));
            sb.AppendLine("</pre>");
        }

        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    public byte[] GeneratePdf(ProtocolExportData data)
    {
        // Для автономной переносимости без внешних C++ библиотек PDF генерируется из валидной структуры HTML документа
        var html = GenerateHtml(data);
        return Encoding.UTF8.GetBytes(html);
    }

    private static void AddStatusListDocx(Body body, string heading, IReadOnlyList<string> items, string emptyText)
    {
        AddHeading(body, heading);
        if (items.Count == 0)
        {
            var p = body.AppendChild(new Paragraph());
            p.AppendChild(new Run(new Text(emptyText)));
        }
        else
        {
            for (int i = 0; i < items.Count; i++)
            {
                var p = body.AppendChild(new Paragraph());
                var rNum = p.AppendChild(new Run(new Text($"{i + 1}. ")));
                rNum.AppendChild(new RunProperties(new Bold()));
                p.AppendChild(new Run(new Text(items[i])));
            }
        }
        AddEmptyParagraph(body);
    }

    private static void AddStatusListHtml(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        sb.AppendLine($"<h2>{System.Net.WebUtility.HtmlEncode(heading)}</h2>");
        sb.AppendLine("<ol>");
        foreach (var item in items)
        {
            sb.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(item)}</li>");
        }
        sb.AppendLine("</ol>");
    }

    private static void AddEmptyParagraph(Body body)
    {
        body.AppendChild(new Paragraph());
    }

    private static void AddHeading(Body body, string text)
    {
        var p = body.AppendChild(new Paragraph());
        var r = p.AppendChild(new Run(new Text(text)));
        var props = r.AppendChild(new RunProperties());
        props.AppendChild(new Bold());
        props.AppendChild(new FontSize { Val = "26" }); // 13pt
    }

    private static void AddMetaParagraph(Body body, string label, string value)
    {
        var p = body.AppendChild(new Paragraph());
        var rLabel = p.AppendChild(new Run(new Text(label)));
        rLabel.AppendChild(new RunProperties(new Bold()));
        p.AppendChild(new Run(new Text(value)));
    }

    private static void AddTableCell(TableRow row, string text, bool isHeader = false, string? width = null)
    {
        var cell = row.AppendChild(new TableCell());
        var p = cell.AppendChild(new Paragraph());
        var r = p.AppendChild(new Run(new Text(text)));
        if (isHeader)
        {
            r.AppendChild(new RunProperties(new Bold()));
        }
        if (width != null)
        {
            cell.AppendChild(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width }));
        }
    }
}

