using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

// L2: lots were create-only. This adds the correction path (expiry / supplier-lot / notes).
public record UpdateLotRecordCommand(int Id, UpdateLotRecordRequestModel Data) : IRequest<LotRecordResponseModel>;

public class UpdateLotRecordCommandValidator : AbstractValidator<UpdateLotRecordCommand>
{
    public UpdateLotRecordCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.SupplierLotNumber).MaximumLength(100).When(x => x.Data.SupplierLotNumber is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
    }
}

public class UpdateLotRecordHandler(AppDbContext db) : IRequestHandler<UpdateLotRecordCommand, LotRecordResponseModel>
{
    public async Task<LotRecordResponseModel> Handle(UpdateLotRecordCommand request, CancellationToken cancellationToken)
    {
        var lot = await db.LotRecords.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot {request.Id} not found");

        var data = request.Data;
        lot.ExpirationDate = data.ExpirationDate;
        lot.SupplierLotNumber = data.SupplierLotNumber?.Trim();
        lot.Notes = data.Notes?.Trim();

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
}
