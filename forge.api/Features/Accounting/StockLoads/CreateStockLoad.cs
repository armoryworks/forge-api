using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>
/// Creates a draft opening stock load from JSON lines or the CSV template's text
/// (<c>partNumber, location, lot, expirationDate, quantity, unitCost</c>). Rows whose part or location cannot be
/// found are refused here; everything else is checked by the dry run.
/// </summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record CreateStockLoadCommand(
    int? BookId,
    DateOnly AsOfDate,
    StockLoadGlMode GlMode,
    string? Notes,
    string? SourceFileName,
    IReadOnlyList<StockLoadLineInput>? Lines,
    string? CsvContent = null) : IRequest<StockLoadModel>;

public class CreateStockLoadValidator : AbstractValidator<CreateStockLoadCommand>
{
    public CreateStockLoadValidator()
    {
        RuleFor(x => x.AsOfDate).NotEmpty();
        RuleFor(x => x.GlMode).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.SourceFileName).MaximumLength(260);
        RuleFor(x => x.Lines).Must(l => l is null || l.Count <= StockLoadLimits.MaxLines)
            .WithMessage($"A stock load holds at most {StockLoadLimits.MaxLines} lines.");
        RuleFor(x => x.CsvContent).MaximumLength(StockLoadLimits.MaxCsvLength);
    }
}

public class CreateStockLoadHandler(AppDbContext db, IStockLoadService stockLoads, IHttpContextAccessor httpContext)
    : IRequestHandler<CreateStockLoadCommand, StockLoadModel>
{
    public async Task<StockLoadModel> Handle(CreateStockLoadCommand request, CancellationToken ct)
    {
        var bookId = request.BookId ?? await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("No active accounting book exists.");
        if (!await db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId && b.IsActive, ct))
            throw new KeyNotFoundException($"Book {bookId} not found or inactive.");

        var input = StockLoadCsvParser.FromRequest(request.Lines, request.CsvContent);
        if (input.Count > StockLoadLimits.MaxLines)
            throw new ValidationException($"A stock load holds at most {StockLoadLimits.MaxLines} lines.");
        var lines = await stockLoads.ResolveLinesAsync(input, ct);
        var userId = int.TryParse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (int?)null;

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var load = new InventoryStockLoad
        {
            LoadNumber = await stockLoads.NextLoadNumberAsync(ct),
            BookId = bookId,
            AsOfDate = request.AsOfDate,
            GlMode = request.GlMode,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            SourceFileName = string.IsNullOrWhiteSpace(request.SourceFileName) ? null : request.SourceFileName.Trim(),
            TotalValue = lines.Sum(l => l.ExtendedValue),
            CreatedByUserId = userId,
            Lines = lines,
        };
        db.InventoryStockLoads.Add(load);
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        return await stockLoads.GetModelAsync(load.Id, ct);
    }
}
