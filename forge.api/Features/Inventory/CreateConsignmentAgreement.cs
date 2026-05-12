using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record CreateConsignmentAgreementCommand(CreateConsignmentAgreementRequestModel Request) : IRequest<ConsignmentAgreementResponseModel>;

public class CreateConsignmentAgreementHandler(AppDbContext db) : IRequestHandler<CreateConsignmentAgreementCommand, ConsignmentAgreementResponseModel>
{
    public async Task<ConsignmentAgreementResponseModel> Handle(CreateConsignmentAgreementCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        if (request.Direction == ConsignmentDirection.Inbound && !request.VendorId.HasValue)
            throw new InvalidOperationException("Inbound consignment requires a VendorId");

        if (request.Direction == ConsignmentDirection.Outbound && !request.CustomerId.HasValue)
            throw new InvalidOperationException("Outbound consignment requires a CustomerId");

        var agreement = new ConsignmentAgreement
        {
            Direction = request.Direction,
            VendorId = request.VendorId,
            CustomerId = request.CustomerId,
            PartId = request.PartId,
            AgreedUnitPrice = request.AgreedUnitPrice,
            MinStockQuantity = request.MinStockQuantity,
            MaxStockQuantity = request.MaxStockQuantity,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            InvoiceOnConsumption = request.InvoiceOnConsumption,
            Status = ConsignmentAgreementStatus.Active,
            Terms = request.Terms,
            ReconciliationFrequencyDays = request.ReconciliationFrequencyDays,
        };

        db.ConsignmentAgreements.Add(agreement);
        await db.SaveChangesAsync(cancellationToken);

        return new ConsignmentAgreementResponseModel
        {
            Id = agreement.Id,
            Direction = agreement.Direction,
            VendorId = agreement.VendorId,
            CustomerId = agreement.CustomerId,
            PartId = agreement.PartId,
            PartNumber = part.PartNumber,
            PartDescription = part.Description ?? part.Name,
            AgreedUnitPrice = agreement.AgreedUnitPrice,
            MinStockQuantity = agreement.MinStockQuantity,
            MaxStockQuantity = agreement.MaxStockQuantity,
            EffectiveFrom = agreement.EffectiveFrom,
            EffectiveTo = agreement.EffectiveTo,
            InvoiceOnConsumption = agreement.InvoiceOnConsumption,
            Status = agreement.Status,
            Terms = agreement.Terms,
            ReconciliationFrequencyDays = agreement.ReconciliationFrequencyDays,
            TransactionCount = 0,
            CreatedAt = agreement.CreatedAt,
        };
    }
}
