using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record UpdateConsignmentAgreementCommand(int Id, UpdateConsignmentAgreementRequestModel Request) : IRequest<ConsignmentAgreementResponseModel>;

public class UpdateConsignmentAgreementHandler(AppDbContext db) : IRequestHandler<UpdateConsignmentAgreementCommand, ConsignmentAgreementResponseModel>
{
    public async Task<ConsignmentAgreementResponseModel> Handle(UpdateConsignmentAgreementCommand command, CancellationToken cancellationToken)
    {
        var agreement = await db.ConsignmentAgreements
            .Include(a => a.Vendor)
            .Include(a => a.Customer)
            .Include(a => a.Part)
            .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Consignment agreement {command.Id} not found");

        var request = command.Request;

        if (request.AgreedUnitPrice.HasValue) agreement.AgreedUnitPrice = request.AgreedUnitPrice.Value;
        if (request.MinStockQuantity.HasValue) agreement.MinStockQuantity = request.MinStockQuantity.Value;
        if (request.MaxStockQuantity.HasValue) agreement.MaxStockQuantity = request.MaxStockQuantity.Value;
        if (request.EffectiveTo.HasValue) agreement.EffectiveTo = request.EffectiveTo.Value;
        if (request.InvoiceOnConsumption.HasValue) agreement.InvoiceOnConsumption = request.InvoiceOnConsumption.Value;
        if (request.Status.HasValue) agreement.Status = request.Status.Value;
        if (request.Terms is not null) agreement.Terms = request.Terms;
        if (request.ReconciliationFrequencyDays.HasValue) agreement.ReconciliationFrequencyDays = request.ReconciliationFrequencyDays.Value;

        await db.SaveChangesAsync(cancellationToken);

        return new ConsignmentAgreementResponseModel
        {
            Id = agreement.Id,
            Direction = agreement.Direction,
            VendorId = agreement.VendorId,
            VendorName = agreement.Vendor?.CompanyName,
            CustomerId = agreement.CustomerId,
            CustomerName = agreement.Customer?.Name,
            PartId = agreement.PartId,
            PartNumber = agreement.Part.PartNumber,
            PartDescription = agreement.Part.Description ?? agreement.Part.Name,
            AgreedUnitPrice = agreement.AgreedUnitPrice,
            MinStockQuantity = agreement.MinStockQuantity,
            MaxStockQuantity = agreement.MaxStockQuantity,
            EffectiveFrom = agreement.EffectiveFrom,
            EffectiveTo = agreement.EffectiveTo,
            InvoiceOnConsumption = agreement.InvoiceOnConsumption,
            Status = agreement.Status,
            Terms = agreement.Terms,
            ReconciliationFrequencyDays = agreement.ReconciliationFrequencyDays,
            TransactionCount = await db.ConsignmentTransactions.CountAsync(t => t.AgreementId == agreement.Id, cancellationToken),
            CreatedAt = agreement.CreatedAt,
        };
    }
}
