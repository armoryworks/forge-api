using System.Text;
using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestInfra = QuestPDF.Infrastructure;

namespace Forge.Api.Features.ComplianceForms;

public record DownloadSubmissionPdfQuery(
    int SubmissionId,
    int RequestingUserId,
    bool IsPrivileged) : IRequest<SubmissionPdfResult>;

public record SubmissionPdfResult(byte[] Bytes, string FileName);

public class DownloadSubmissionPdfHandler(AppDbContext db, IStorageService storageService)
    : IRequestHandler<DownloadSubmissionPdfQuery, SubmissionPdfResult>
{
    public async Task<SubmissionPdfResult> Handle(
        DownloadSubmissionPdfQuery request, CancellationToken ct)
    {
        var submission = await db.ComplianceFormSubmissions
            .Include(s => s.Template)
            .Include(s => s.FormDefinitionVersion)
            .Include(s => s.SignedPdfFile)
            .FirstOrDefaultAsync(
                s => s.Id == request.SubmissionId
                     && (request.IsPrivileged || s.UserId == request.RequestingUserId), ct)
            ?? throw new KeyNotFoundException($"Submission {request.SubmissionId} not found.");

        var safeName = new string(
            (submission.Template?.Name ?? "form")
            .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == ' ' ? c : '_')
            .ToArray()).Trim();
        var fileName = $"{safeName}.pdf";

        // If a signed PDF was already stored (DocuSeal webhook), stream it directly
        if (submission.SignedPdfFile is not null)
        {
            using var stream = await storageService.DownloadAsync(
                submission.SignedPdfFile.BucketName,
                submission.SignedPdfFile.ObjectKey, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return new SubmissionPdfResult(ms.ToArray(), fileName);
        }

        // Generate on-demand from form definition + submitted data
        var bytes = GeneratePdf(submission);
        return new SubmissionPdfResult(bytes, fileName);
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private record ParsedField(string Id, string Label, string Type);
    private record ParsedSection(string? Title, List<ParsedField> Fields);
    private record ParsedPage(string? Title, List<ParsedSection> Sections);

    private static byte[] GeneratePdf(Core.Entities.ComplianceFormSubmission submission)
    {
        var formName = submission.Template?.Name ?? "Compliance Form";
        var submittedOn = submission.SignedAt?.ToString("MM/dd/yyyy") ?? DateTimeOffset.UtcNow.ToString("MM/dd/yyyy");

        // Parse submitted data
        var formData = ParseFormData(submission.FormDataJson);

        // Parse definition from pinned version, falling back to latest on template
        var defJson = submission.FormDefinitionVersion?.FormDefinitionJson;
        var pages = string.IsNullOrEmpty(defJson) ? [] : ParsePages(defJson);

        using var ms = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(1, QuestInfra.Unit.Inch);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial"));

                // ── Header ────────────────────────────────────────────────
                page.Header().Column(col =>
                {
                    col.Item().Text(formName).FontSize(16).SemiBold();
                    col.Item().Text($"Submitted: {submittedOn}")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                // ── Content ───────────────────────────────────────────────
                page.Content().PaddingTop(12).Column(col =>
                {
                    if (pages.Count == 0 || formData.Count == 0)
                    {
                        col.Item().PaddingTop(20)
                            .Text("This form was acknowledged/completed electronically.")
                            .FontColor(Colors.Grey.Medium);
                        return;
                    }

                    foreach (var pg in pages)
                    {
                        if (pages.Count > 1 && !string.IsNullOrEmpty(pg.Title))
                            col.Item().PaddingTop(10).Text(pg.Title).FontSize(12).SemiBold();

                        foreach (var section in pg.Sections)
                        {
                            var sectionHasValues = section.Fields.Any(f =>
                                !IsDisplayOnly(f.Type) && formData.ContainsKey(f.Id));

                            if (!sectionHasValues) continue;

                            if (!string.IsNullOrEmpty(section.Title))
                                col.Item().PaddingTop(14).PaddingBottom(2)
                                    .Text(section.Title)
                                    .FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);

                            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

                            foreach (var field in section.Fields)
                            {
                                if (IsDisplayOnly(field.Type)) continue;
                                if (!formData.TryGetValue(field.Id, out var val)) continue;

                                var display = GetDisplayValue(val, field.Type);
                                if (string.IsNullOrWhiteSpace(display)) continue;

                                col.Item().PaddingVertical(3).Row(row =>
                                {
                                    row.RelativeItem(2)
                                        .PaddingRight(8)
                                        .Text(field.Label)
                                        .FontSize(9)
                                        .FontColor(Colors.Grey.Darken2);

                                    row.RelativeItem(3)
                                        .Text(display)
                                        .FontSize(10);
                                });
                            }
                        }
                    }
                });

                // ── Footer ────────────────────────────────────────────────
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated by QB Engineer  ·  Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf(ms);

        return ms.ToArray();
    }

    private static Dictionary<string, JsonElement> ParseFormData(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<ParsedPage> ParsePages(string defJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(defJson);
            var root = doc.RootElement;

            List<ParsedPage> pages = [];

            if (root.TryGetProperty("pages", out var pagesEl))
            {
                foreach (var pg in pagesEl.EnumerateArray())
                {
                    var title = pg.TryGetProperty("title", out var t) ? t.GetString() : null;
                    pages.Add(new ParsedPage(title, ParseSections(pg)));
                }
            }
            else if (root.TryGetProperty("sections", out var sectionsEl))
            {
                pages.Add(new ParsedPage(null, ParseSections(root)));
            }

            return pages;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<ParsedSection> ParseSections(JsonElement parent)
    {
        List<ParsedSection> sections = [];
        if (!parent.TryGetProperty("sections", out var sectionsEl)) return sections;

        foreach (var sec in sectionsEl.EnumerateArray())
        {
            var title = sec.TryGetProperty("title", out var t) ? t.GetString() : null;
            List<ParsedField> fields = [];

            if (sec.TryGetProperty("fields", out var fieldsEl))
            {
                foreach (var field in fieldsEl.EnumerateArray())
                {
                    if (!field.TryGetProperty("id", out var idEl)) continue;
                    var id = idEl.GetString();
                    if (string.IsNullOrEmpty(id)) continue;

                    var label = field.TryGetProperty("label", out var lbl)
                        ? lbl.GetString() ?? id
                        : id;
                    var type = field.TryGetProperty("type", out var tp)
                        ? tp.GetString() ?? "text"
                        : "text";

                    fields.Add(new ParsedField(id, label, type));
                }
            }

            sections.Add(new ParsedSection(title, fields));
        }

        return sections;
    }

    private static bool IsDisplayOnly(string type) =>
        type is "heading" or "paragraph" or "html";

    private static string GetDisplayValue(JsonElement val, string fieldType) =>
        fieldType switch
        {
            "checkbox" or "toggle" =>
                val.ValueKind == JsonValueKind.True ? "Yes" :
                val.ValueKind == JsonValueKind.False ? "No" : "",
            _ => val.ValueKind switch
            {
                JsonValueKind.String => val.GetString() ?? "",
                JsonValueKind.True => "Yes",
                JsonValueKind.False => "No",
                JsonValueKind.Number => val.GetRawText(),
                _ => "",
            },
        };
}
