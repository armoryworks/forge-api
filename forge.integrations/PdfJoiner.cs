using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Forge.Integrations;

/// <summary>Joins PDF files into one, pages in order. The first file's title and author carry over.</summary>
public static class PdfJoiner
{
    public static byte[] Join(IReadOnlyList<byte[]> files)
    {
        if (files.Count == 0)
            throw new ArgumentException("There is nothing to join.", nameof(files));
        if (files.Count == 1)
            return files[0];

        using var joined = new PdfDocument();
        for (var f = 0; f < files.Count; f++)
        {
            using var stream = new MemoryStream(files[f]);
            using var source = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            if (f == 0)
            {
                joined.Info.Title = source.Info.Title;
                joined.Info.Author = source.Info.Author;
            }
            for (var p = 0; p < source.PageCount; p++)
                joined.AddPage(source.Pages[p]);
        }

        using var output = new MemoryStream();
        joined.Save(output, false);
        return output.ToArray();
    }
}
