using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Dry-run for a VendorPart CSV bulk import. Parses the file, classifies each
/// row as Add / Update / Error, returns the full proposal for the UI's preview
/// table. NEVER mutates the database — pure read.
/// </summary>
public record PreviewVendorPartImportCommand(
    int VendorId,
    IFormFile File) : IRequest<VendorPartImportPreviewResponseModel>;

public class PreviewVendorPartImportHandler(AppDbContext db)
    : IRequestHandler<PreviewVendorPartImportCommand, VendorPartImportPreviewResponseModel>
{
    public async Task<VendorPartImportPreviewResponseModel> Handle(
        PreviewVendorPartImportCommand request, CancellationToken ct)
    {
        if (request.File is null || request.File.Length == 0)
            throw new InvalidOperationException("CSV file is required");

        if (!await db.Vendors.AnyAsync(v => v.Id == request.VendorId, ct))
            throw new KeyNotFoundException($"Vendor {request.VendorId} not found");

        await using var stream = request.File.OpenReadStream();
        var rawRows = VendorPartCsvParser.Parse(stream);

        var rows = await VendorPartImportClassifier.ClassifyAsync(db, request.VendorId, rawRows, ct);

        return new VendorPartImportPreviewResponseModel(
            TotalRows: rows.Count,
            AddCount: rows.Count(r => r.Action == BulkImportRowAction.Add),
            UpdateCount: rows.Count(r => r.Action == BulkImportRowAction.Update),
            ErrorCount: rows.Count(r => r.Action == BulkImportRowAction.Error),
            Rows: rows);
    }
}
