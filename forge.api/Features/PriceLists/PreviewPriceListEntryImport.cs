using MediatR;

using Microsoft.AspNetCore.Http;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PriceLists;

/// <summary>
/// Dry-run for a PriceListEntry CSV bulk import. Parses the file, classifies
/// each row as Add / Update / Skip / Error, returns the full proposal for
/// the UI's preview table. NEVER mutates the database — pure read.
/// </summary>
public record PreviewPriceListEntryImportCommand(
    int PriceListId,
    IFormFile File) : IRequest<BulkImportPreviewResponseModel>;

public class PreviewPriceListEntryImportHandler(
    AppDbContext db,
    IPriceListRepository repo,
    ICurrencyService currency)
    : IRequestHandler<PreviewPriceListEntryImportCommand, BulkImportPreviewResponseModel>
{
    public async Task<BulkImportPreviewResponseModel> Handle(
        PreviewPriceListEntryImportCommand request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
            throw new InvalidOperationException("CSV file is required");

        if (!await repo.PriceListExistsAsync(request.PriceListId, cancellationToken))
            throw new KeyNotFoundException($"Price list {request.PriceListId} not found");

        var baseCurrency = await currency.GetBaseCurrencyAsync(cancellationToken);

        // Parse + classify. The parser tolerates malformed cells (per-row
        // errors flow through to the preview rather than aborting the batch).
        await using var stream = request.File.OpenReadStream();
        var rawRows = PriceListEntryCsvParser.Parse(stream);

        var rows = await PriceListEntryImportClassifier.ClassifyAsync(
            db, request.PriceListId, rawRows, baseCurrency, cancellationToken);

        return new BulkImportPreviewResponseModel(
            TotalRows: rows.Count,
            AddCount: rows.Count(r => r.Action == BulkImportRowAction.Add),
            UpdateCount: rows.Count(r => r.Action == BulkImportRowAction.Update),
            ErrorCount: rows.Count(r => r.Action == BulkImportRowAction.Error),
            Rows: rows);
    }
}
