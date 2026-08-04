using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace JobApplicationBot.Services;

public interface ICvTextExtractionService
{
    /// <summary>
    /// Extracts plain text from a CV file. Supports PDF and DOCX. Returns empty string if unsupported/empty.
    /// </summary>
    string ExtractText(byte[] content, string fileName, string? contentType = null);
}

public class CvTextExtractionService : ICvTextExtractionService
{
    private const int MaxChars = 20000;

    public string ExtractText(byte[] content, string fileName, string? contentType = null)
    {
        if (content.Length == 0)
            return string.Empty;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string text = ext switch
        {
            ".pdf" => ExtractFromPdf(content),
            ".docx" => ExtractFromDocx(content),
            ".doc" => throw new NotSupportedException(
                "Old .doc files are not supported for text extraction. Please upload a PDF or DOCX."),
            _ => throw new NotSupportedException($"Unsupported CV type '{ext}'. Upload a PDF or DOCX.")
        };

        text = NormalizeWhitespace(text);
        if (text.Length > MaxChars)
            text = text[..MaxChars];

        return text;
    }

    private static string ExtractFromPdf(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfDocument.Open(stream);
        var parts = new List<string>();
        foreach (Page page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
                parts.Add(pageText);
        }

        return string.Join("\n", parts);
    }

    private static string ExtractFromDocx(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var word = WordprocessingDocument.Open(stream, false);
        var body = word.MainDocumentPart?.Document?.Body;
        if (body == null)
            return string.Empty;

        return body.InnerText;
    }

    private static string NormalizeWhitespace(string text)
    {
        var lines = text
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l));

        return string.Join("\n", lines).Trim();
    }
}
