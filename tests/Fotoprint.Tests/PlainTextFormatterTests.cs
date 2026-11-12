using Fotoprint.Core.Formatting;
using Xunit;

namespace Fotoprint.Tests;

public class PlainTextFormatterTests
{
    [Fact]
    public void Sanitize_StripsBoldItalicHeadingsCodeAndLinks_WhilePreservingReadability()
    {
        var input = """
            ### 📊 **Сводный отчёт** по заказу №44102

            * Клиент: **ООО «Вектор»**
            * Статус: _В печати_ (`Heidelberg`)
            * Подробнее: [Открыть в Битрикс24](https://b24.example.com/task/42)

            ```json
            {"id": 44102, "status": "ok"}
            ```

            [Источник: 1С:УНФ, 12.11.2026]
            """;

        var sanitized = PlainTextFormatter.Sanitize(input);

        Assert.DoesNotContain("**", sanitized);
        Assert.DoesNotContain("###", sanitized);
        Assert.DoesNotContain("`", sanitized);
        Assert.DoesNotContain("[Открыть в Битрикс24](", sanitized);
        Assert.DoesNotContain("_В печати_", sanitized);
        Assert.Contains("📊 Сводный отчёт по заказу №44102", sanitized);
        Assert.Contains("• Клиент: ООО «Вектор»", sanitized);
        Assert.Contains("• Статус: В печати (Heidelberg)", sanitized);
        Assert.Contains("Открыть в Битрикс24 (https://b24.example.com/task/42)", sanitized);
        Assert.Contains("{\"id\": 44102, \"status\": \"ok\"}", sanitized);
        Assert.Contains("[Источник: 1С:УНФ, 12.11.2026]", sanitized);
    }

    [Fact]
    public void Sanitize_PreservesSnakeCaseIdentifiersAndMathExpressions()
    {
        var input = "Вызовите external_user_id и get_order_status; расчёт: 2 * 3 = 6.";

        var sanitized = PlainTextFormatter.Sanitize(input);

        Assert.Equal("Вызовите external_user_id и get_order_status; расчёт: 2 * 3 = 6.", sanitized);
    }

    [Fact]
    public void Sanitize_RemovesMarkdownTablesAndBlockquotesCleanly()
    {
        var input = """
            > Важное примечание по тиражу:

            | Машина | Статус | Загрузка |
            | --- | --- | --- |
            | **Heidelberg** | Работает | `87%` |
            """;

        var sanitized = PlainTextFormatter.Sanitize(input);

        Assert.DoesNotContain("---", sanitized);
        Assert.DoesNotContain("**", sanitized);
        Assert.DoesNotContain("`", sanitized);
        Assert.DoesNotContain("> ", sanitized);
        Assert.Contains("Важное примечание по тиражу:", sanitized);
        Assert.Contains("Машина | Статус | Загрузка", sanitized);
        Assert.Contains("Heidelberg | Работает | 87%", sanitized);
    }

    [Fact]
    public void Sanitize_HandlesNullAndWhitespace()
    {
        Assert.Equal(string.Empty, PlainTextFormatter.Sanitize(null));
        Assert.Equal(string.Empty, PlainTextFormatter.Sanitize("   \r\n  "));
    }
}
