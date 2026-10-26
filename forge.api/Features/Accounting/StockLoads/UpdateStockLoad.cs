using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>Replaces a draft load's date, mode, notes and lines (the whole set; CSV text or JSON lines).</summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record UpdateStockLoadCommand(
    int Id,
    DateOnly AsOfDate,
    StockLoadGlMode GlMode,
    string? Notes,
    string? SourceFileName,
    IReadOnlyList<StockLoadLineInput>? Lines,
    string? CsvContent = null) : IRequest<StockLoadModel>;

public class UpdateStockLoadValidator : AbstractValidator<UpdateStockLoadCommand>
{
    public UpdateStockLoadValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.AsOfDate).NotEmpty();
        RuleFor(x => x.GlMode).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.SourceFileName).MaximumLength(260);
        RuleFor(x => x.Lines).Must(l => l is null || l.Count <= StockLoadLimits.MaxLines)
            .WithMessage($"A stock load holds at most {StockLoadLimits.MaxLines} lines.");
        RuleFor(x => x.CsvContent).MaximumLength(StockLoadLimits.MaxCsvLength);
    }
}

public class UpdateStockLoadHandler(AppDbContext db, IStockLoadService stockLoads)
    : IRequestHandler<UpdateStockLoadCommand, StockLoadModel>
{
    public async Task<StockLoadModel> Handle(UpdateStockLoadCommand request, CancellationToken ct)
    {
        var load = await db.InventoryStockLoads.Include(l => l.Lines)
            .FirstOrDefaultAsync(l => l.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Stock load {request.Id} not found.");
        if (load.Status != StockLoadStatus.Draft)
            throw new InvalidOperationException($"Stock load {load.LoadNumber} is {load.Status}; only a draft can be changed.");

        var input = StockLoadCsvParser.FromRequest(request.Lines, request.CsvContent);
        if (input.Count > StockLoadLimits.MaxLines)
            throw new ValidationException($"A stock load holds at most {StockLoadLimits.MaxLines} lines.");
        var lines = await stockLoads.ResolveLinesAsync(input, ct);

        db.InventoryStockLoadLines.RemoveRange(load.Lines);
        load.Lines.Clear();
        foreach (var line in lines)
            load.Lines.Add(line);
        load.AsOfDate = request.AsOfDate;
        load.GlMode = request.GlMode;
        load.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (request.SourceFileName is not null)
            load.SourceFileName = string.IsNullOrWhiteSpace(request.SourceFileName) ? null : request.SourceFileName.Trim();
        load.TotalValue = lines.Sum(l => l.ExtendedValue);
        await db.SaveChangesAsync(ct);

        return await stockLoads.GetModelAsync(load.Id, ct);
    }
}
