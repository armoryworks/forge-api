using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

public record CreateLotRecordCommand(CreateLotRecordRequestModel Data) : IRequest<LotRecordResponseModel>;

public class CreateLotRecordCommandValidator : AbstractValidator<CreateLotRecordCommand>
{
    public CreateLotRecordCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        // Phase 3 / WU-23 (F8-broad): decimal quantity supports fractional UoM.
        RuleFor(x => x.Data.Quantity).GreaterThan(0m);
        RuleFor(x => x.Data.LotNumber).MaximumLength(100).When(x => x.Data.LotNumber is not null);
        RuleFor(x => x.Data.SupplierLotNumber).MaximumLength(100).When(x => x.Data.SupplierLotNumber is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
    }
}

public class CreateLotRecordHandler(AppDbContext db)
    : IRequestHandler<CreateLotRecordCommand, LotRecordResponseModel>
{
    public async Task<LotRecordResponseModel> Handle(
        CreateLotRecordCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;

        var lotNumber = data.LotNumber?.Trim();
        if (string.IsNullOrWhiteSpace(lotNumber))
            lotNumber = await GenerateLotNumber(cancellationToken);

        var lot = new LotRecord
        {
            LotNumber = lotNumber,
            PartId = data.PartId,
            JobId = data.JobId,
            ProductionRunId = data.ProductionRunId,
            PurchaseOrderLineId = data.PurchaseOrderLineId,
            Quantity = data.Quantity,
            ExpirationDate = data.ExpirationDate,
            SupplierLotNumber = data.SupplierLotNumber?.Trim(),
            Notes = data.Notes?.Trim(),
        };

        db.LotRecords.Add(lot);
        await db.SaveChangesAsync(cancellationToken);

        return await db.LotRecords
            .AsNoTracking()
            .Include(l => l.Part)
            .Include(l => l.Job)
            .Where(l => l.Id == lot.Id)
            .Select(l => new LotRecordResponseModel(
                l.Id,
                l.LotNumber,
                l.PartId,
                l.Part.PartNumber,
                l.Part.Description,
                l.JobId,
                l.Job != null ? l.Job.JobNumber : null,
                l.ProductionRunId,
                l.PurchaseOrderLineId,
                l.Quantity,
                l.ExpirationDate,
                l.SupplierLotNumber,
                l.Notes,
                l.CreatedAt))
            .FirstAsync(cancellationToken);
    }

    private async Task<string> GenerateLotNumber(CancellationToken cancellationToken)
    {
        var datePrefix = $"LOT-{DateTimeOffset.UtcNow:yyyyMMdd}";
        var todayCount = await db.LotRecords
            .CountAsync(l => l.LotNumber.StartsWith(datePrefix), cancellationToken);
        return $"{datePrefix}-{(todayCount + 1):D3}";
    }
}
