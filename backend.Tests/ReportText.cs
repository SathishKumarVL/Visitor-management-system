using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Pulls readable text out of exported report bytes so tenant isolation can be asserted on the
/// artefact a customer actually downloads rather than on the query that produced it.
/// </summary>
internal static class ReportText
{
    public static string FromPlainText(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>
    /// An .xlsx is a zip of XML parts; cell strings live in sharedStrings.xml and inline strings in
    /// the sheet parts, so concatenating every XML part covers both.
    /// </summary>
    public static string FromXlsx(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var sb = new StringBuilder();
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            sb.AppendLine(reader.ReadToEnd());
        }
        return sb.ToString();
    }

    /// <summary>
    /// The report generator subsets its fonts, so text is stored as glyph ids rather than readable
    /// bytes. PdfPig resolves the embedded encoding and gives back the words as rendered.
    /// </summary>
    public static string FromPdf(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            // Words, not raw page text: the generator positions each cell separately, so raw text
            // can interleave columns while word extraction keeps values intact.
            sb.AppendLine(string.Join(" ", page.GetWords().Select(w => w.Text)));
        }
        return sb.ToString();
    }
}
