using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

using Forge.Api.Features.Ai;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Ai;

/// <summary>
/// A PDF attachment (the usual way a shop stores work instructions) has its body indexed for RAG, not just
/// its file name; an unreadable PDF still leaves the file-name chunk.
/// </summary>
public class IndexDocumentPdfTests
{
    private static byte[] BuildPdf(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(text, 12, new PdfPoint(40, 700), font);
        }
        return builder.Build();
    }

    private static async Task<(int Result, List<DocumentEmbedding> Upserted)> IndexAsync(
        byte[] body, string contentType, string fileName, IPdfTextExtractor extractor)
    {
        await using var db = TestDbContextFactory.Create();
        var file = new FileAttachment
        {
            FileName = fileName,
            ContentType = contentType,
            Size = body.Length,
            BucketName = "forge-files",
            ObjectKey = "parts/1/" + fileName,
            EntityType = "Part",
            EntityId = 1,
        };
        db.FileAttachments.Add(file);
        await db.SaveChangesAsync();

        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.DownloadAsync("forge-files", file.ObjectKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(body));
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[384]);
        var upserted = new List<DocumentEmbedding>();
        var repo = new Mock<IEmbeddingRepository>();
        repo.Setup(r => r.UpsertEmbeddingsAsync("FileAttachment", file.Id, It.IsAny<List<DocumentEmbedding>>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, List<DocumentEmbedding>, CancellationToken>((_, _, e, _) => upserted.AddRange(e))
            .Returns(Task.CompletedTask);

        var handler = new IndexDocumentHandler(db, ai.Object, repo.Object, storage.Object, extractor,
            NullLogger<IndexDocumentHandler>.Instance);
        var result = await handler.Handle(new IndexDocumentCommand("FileAttachment", file.Id), default);
        return (result, upserted);
    }

    [Fact]
    public async Task Pdf_attachment_body_is_indexed_as_content()
    {
        var pdf = BuildPdf("Work instruction: clamp the bracket in fixture QF-FX-12", "Torque the bolts to 25 Nm");

        var (_, upserted) = await IndexAsync(pdf, "application/pdf", "WI-100.pdf", new PdfPigTextExtractor());

        upserted.Should().Contain(e => e.SourceField == "FileName" && e.ChunkText == "WI-100.pdf");
        var content = string.Join(" ", upserted.Where(e => e.SourceField == "Content").Select(e => e.ChunkText));
        content.Should().Contain("QF-FX-12").And.Contain("25 Nm");
    }

    [Fact]
    public async Task Pdf_is_recognised_by_extension_when_the_content_type_is_generic()
    {
        var pdf = BuildPdf("fixture QF-FX-12");

        var (_, upserted) = await IndexAsync(pdf, "application/octet-stream", "WI-100.PDF", new PdfPigTextExtractor());

        upserted.Should().Contain(e => e.SourceField == "Content" && e.ChunkText.Contains("QF-FX-12"));
    }

    [Fact]
    public async Task Unreadable_pdf_still_indexes_the_file_name()
    {
        var extractor = new Mock<IPdfTextExtractor>();
        extractor.Setup(x => x.ExtractText(It.IsAny<byte[]>(), It.IsAny<int>())).Throws(new InvalidOperationException("bad pdf"));

        var (result, upserted) = await IndexAsync([1, 2, 3], "application/pdf", "broken.pdf", extractor.Object);

        result.Should().Be(1);
        upserted.Should().ContainSingle(e => e.SourceField == "FileName");
    }

    [Fact]
    public void Extractor_stops_at_the_character_cap()
    {
        var pdf = BuildPdf("alpha beta gamma delta", "epsilon zeta");

        var text = new PdfPigTextExtractor().ExtractText(pdf, 10);

        text.Should().HaveLength(10);
    }
}
