using Fotoprint.Core.Documents;
using Fotoprint.Core.Protocols;
using Xunit;

namespace Fotoprint.Tests;

public class DocumentParserAndProtocolTests
{
    [Fact]
    public void DocumentParser_Chunks_Text_With_Overlap()
    {
        var text = @"Технологический регламент офсетного цеха.
Нормы расхода краски и бумаги на машинах Heidelberg Speedmaster.
Допустимый технологический брак не более 2.5%.
Все отклонения оформляются актом смены.";

        var chunks = DocumentParser.ChunkText(text, chunkSize: 80, chunkOverlap: 20);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void MeetingProtocolGenerator_Builds_Docx_And_Html()
    {
        var generator = new MeetingProtocolGenerator();
        var data = new ProtocolExportData(
            Title: "Оперативка производства по тиражу 44102",
            MeetingDate: DateTime.UtcNow,
            OrganizerName: "Смирнов Алексей (Генеральный директор)",
            Participants: new List<string> { "Кузнецов Иван (Производство)", "Морозова Елена (Продажи)" },
            Summary: "Обсуждение причин срыва графика поставки бумаги и мер ликвидации отставания.",
            Decisions: new List<string>
            {
                "Перевести Heidelberg Speedmaster на круглосуточный режим работы.",
                "Срочно заказать валы у поставщика."
            },
            ActionItems: new List<ActionItemExportData>
            {
                new("Сформировать спецификацию на валы", "Кузнецов Иван", DateTime.UtcNow.AddDays(2), "pending"),
                new("Согласовать с клиентом Альфа-Маркет перенос отгрузки на 1 день", "Морозова Елена", DateTime.UtcNow.AddDays(1), "pending")
            }
        );

        // DOCX byte stream
        var docxBytes = generator.GenerateDocx(data);
        Assert.NotNull(docxBytes);
        Assert.True(docxBytes.Length > 0);

        // HTML
        var html = generator.GenerateHtml(data);
        Assert.Contains("Оперативка производства по тиражу 44102", html);
        Assert.Contains("Смирнов Алексей", html);
        Assert.Contains("Heidelberg Speedmaster", html);
        Assert.Contains("Action Items", html);
    }

    [Fact]
    public async Task MeetingProtocolGenerator_Includes_Full_Transcript_When_Provided()
    {
        var generator = new MeetingProtocolGenerator();
        var data = new ProtocolExportData(
            Title: "Созвон Google Meet",
            MeetingDate: DateTime.UtcNow,
            OrganizerName: "Смирнов Алексей",
            Participants: new List<string> { "Кузнецов Иван" },
            Summary: "Краткое саммари.",
            Decisions: new List<string> { "Утвердить смету" },
            ActionItems: new List<ActionItemExportData>(),
            RawTranscript: "Алексей: Коллеги, начинаем.\nИван: Смета готова на 100%."
        );

        var docxBytes = generator.GenerateDocx(data);
        Assert.NotNull(docxBytes);
        Assert.True(docxBytes.Length > 0);

        using var ms = new MemoryStream(docxBytes);
        var parser = new DocumentParser();
        var parsed = await parser.ParseStreamAsync(ms, "meeting.docx");
        Assert.Contains("ПОЛНАЯ СТЕНОГРАММА СОЗВОНА", parsed.FullText);
        Assert.Contains("Смета готова на 100%", parsed.FullText);

        var html = generator.GenerateHtml(data);
        Assert.Contains("Полная стенограмма созвона", html);
        Assert.Contains("Алексей: Коллеги, начинаем.", html);
    }

    [Fact]
    public void MeetingProtocolGenerator_Encodes_Html_Special_Characters_Against_Xss()
    {
        var generator = new MeetingProtocolGenerator();
        var data = new ProtocolExportData(
            Title: "<script>alert('title')</script>",
            MeetingDate: DateTime.UtcNow,
            OrganizerName: "<b>Иван</b>",
            Participants: new List<string> { "<img src=x onerror=alert(1)>" },
            Summary: "<script>alert('summary')</script>",
            Decisions: new List<string> { "<svg onload=alert('decision')>" },
            ActionItems: new List<ActionItemExportData>
            {
                new("<script>alert('task')</script>", "<a href='javascript:alert(1)'>Борис</a>", DateTime.UtcNow, "<script>status</script>")
            },
            RawTranscript: "<script>alert('raw')</script>"
        );

        var html = generator.GenerateHtml(data);

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<svg onload=", html);
        Assert.Contains("&lt;script&gt;alert(&#39;title&#39;)&lt;/script&gt;", html);
        Assert.Contains("&lt;script&gt;alert(&#39;summary&#39;)&lt;/script&gt;", html);
        Assert.Contains("&lt;svg onload=alert(&#39;decision&#39;)&gt;", html);
        Assert.Contains("&lt;script&gt;alert(&#39;task&#39;)&lt;/script&gt;", html);
    }
}

