using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;

namespace Fotoprint.Core.Documents;

public record DocumentChunk(int Index, string Text);

public record ParsedDocument(string Title, string SourceType, string FullText, List<DocumentChunk> Chunks);

public interface IDocumentParser
{
    Task<ParsedDocument> ParseFileAsync(string filePath, string? originalFileName = null, int chunkSize = 800, int chunkOverlap = 150, CancellationToken ct = default);
    Task<ParsedDocument> ParseStreamAsync(Stream stream, string fileName, int chunkSize = 800, int chunkOverlap = 150, CancellationToken ct = default);
}

public class DocumentParser : IDocumentParser
{
    public async Task<ParsedDocument> ParseFileAsync(string filePath, string? originalFileName = null, int chunkSize = 800, int chunkOverlap = 150, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Document file not found: {filePath}");

        var name = originalFileName ?? Path.GetFileName(filePath);
        await using var stream = File.OpenRead(filePath);
        return await ParseStreamAsync(stream, name, chunkSize, chunkOverlap, ct);
    }

    public async Task<ParsedDocument> ParseStreamAsync(Stream stream, string fileName, int chunkSize = 800, int chunkOverlap = 150, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string text;

        switch (ext)
        {
            case ".txt":
                text = await ReadTxtStreamAsync(stream, ct);
                break;
            case ".pdf":
                text = ReadPdfStream(stream);
                break;
            case ".docx":
                text = ReadDocxStream(stream);
                break;
            case ".xlsx":
                text = ReadXlsxStream(stream);
                break;
            default:
                throw new NotSupportedException($"Unsupported document format '{ext}'. Supported: .pdf, .docx, .xlsx, .txt");
        }

        var chunks = ChunkText(text, chunkSize, chunkOverlap);
        return new ParsedDocument(fileName, ext.TrimStart('.'), text, chunks);
    }

    private static async Task<string> ReadTxtStreamAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private static string ReadPdfStream(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString().Trim();
    }

    private static string ReadDocxStream(Stream stream)
    {
        using var wordDoc = WordprocessingDocument.Open(stream, false);
        var body = wordDoc.MainDocumentPart?.Document?.Body;
        if (body == null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var textElem in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
        {
            sb.Append(textElem.Text).Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static string ReadXlsxStream(Stream stream)
    {
        using var spreadSheet = SpreadsheetDocument.Open(stream, false);
        var workbookPart = spreadSheet.WorkbookPart;
        if (workbookPart == null) return string.Empty;

        var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();
            if (sheetData == null) continue;

            foreach (var row in sheetData.Elements<Row>())
            {
                var rowTexts = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var val = cell.CellValue?.Text ?? string.Empty;
                    if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString && sharedStringTable != null)
                    {
                        if (int.TryParse(val, out var ssid))
                        {
                            val = sharedStringTable.ElementAt(ssid).InnerText;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        rowTexts.Add(val.Trim());
                    }
                }
                if (rowTexts.Count > 0)
                {
                    sb.AppendLine(string.Join(" | ", rowTexts));
                }
            }
        }
        return sb.ToString().Trim();
    }

    public static List<DocumentChunk> ChunkText(string fullText, int chunkSize, int chunkOverlap)
    {
        var chunks = new List<DocumentChunk>();
        if (string.IsNullOrWhiteSpace(fullText))
            return chunks;

        var clean = fullText.Replace("\r\n", "\n").Trim();
        var paragraphs = clean.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = new StringBuilder();
        int chunkIndex = 1;

        foreach (var para in paragraphs)
        {
            var trimmedPara = para.Trim();
            if (trimmedPara.Length == 0) continue;

            if (currentChunk.Length + trimmedPara.Length + 1 > chunkSize && currentChunk.Length > 0)
            {
                chunks.Add(new DocumentChunk(chunkIndex++, currentChunk.ToString().Trim()));
                
                // сохраняем перекрытие
                var currentStr = currentChunk.ToString();
                currentChunk.Clear();
                if (currentStr.Length > chunkOverlap)
                {
                    currentChunk.Append(currentStr[^chunkOverlap..]).Append("\n");
                }
            }

            currentChunk.AppendLine(trimmedPara);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(new DocumentChunk(chunkIndex, currentChunk.ToString().Trim()));
        }

        return chunks;
    }
}

