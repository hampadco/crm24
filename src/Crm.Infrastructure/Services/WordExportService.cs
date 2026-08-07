using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Crm.Infrastructure.Services;

/// <summary>تبدیل HTML قالب چاپ به DOCX با AltChunk (Open XML).</summary>
public class WordExportService
{
    /// <summary>ساخت فایل Word از HTML رندرشده.</summary>
    public byte[] HtmlToDocx(string html, string title)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var altPart = main.AddAlternativeFormatImportPart(
                AlternativeFormatImportPartType.Html, "htmlChunk1");
            var payload = EnsureHtmlDocument(html, title);
            using (var stream = altPart.GetStream(FileMode.Create, FileAccess.Write))
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                stream.Write(bytes, 0, bytes.Length);
            }

            var altChunkId = main.GetIdOfPart(altPart);
            var altChunk = new AltChunk { Id = altChunkId };
            main.Document.Body!.Append(altChunk);
            main.Document.Save();
        }

        return ms.ToArray();
    }

    private static string EnsureHtmlDocument(string html, string title)
    {
        if (html.Contains("<html", StringComparison.OrdinalIgnoreCase))
            return html;

        var safeTitle = System.Net.WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(title) ? "Document" : title);
        return $"""
            <!DOCTYPE html>
            <html lang="fa" dir="rtl">
            <head><meta charset="utf-8" /><title>{safeTitle}</title></head>
            <body>{html}</body>
            </html>
            """;
    }
}
