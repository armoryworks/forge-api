using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record SendOutboundEdiCommand(string EntityType, int EntityId, SendOutboundEdiRequestModel Model) : IRequest<EdiTransactionResponseModel>;

public class SendOutboundEdiHandler(AppDbContext db, IEdiService ediService, IMediator mediator)
    : IRequestHandler<SendOutboundEdiCommand, EdiTransactionResponseModel>
{
    public async Task<EdiTransactionResponseModel> Handle(
        SendOutboundEdiCommand request, CancellationToken cancellationToken)
    {
        var transaction = request.EntityType.ToLowerInvariant() switch
        {
            "shipment" => await ediService.GenerateAsnAsync(request.EntityId, request.Model.TradingPartnerId, cancellationToken),
            "invoice" => await ediService.GenerateInvoiceEdiAsync(request.EntityId, request.Model.TradingPartnerId, cancellationToken),
            "sales-order" => await ediService.GeneratePoAckAsync(request.EntityId, request.Model.TradingPartnerId, cancellationToken),
            _ => throw new ArgumentException($"Unsupported entity type: {request.EntityType}")
        };

        db.EdiTransactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

        await ediService.SendTransactionAsync(transaction.Id, cancellationToken);

        var detail = await mediator.Send(new GetEdiTransactionByIdQuery(transaction.Id), cancellationToken);
        return new EdiTransactionResponseModel
        {
            Id = detail.Id,
            TradingPartnerId = detail.TradingPartnerId,
            TradingPartnerName = detail.TradingPartnerName,
            Direction = detail.Direction,
            TransactionSet = detail.TransactionSet,
            ControlNumber = detail.ControlNumber,
            Status = detail.Status,
            RelatedEntityType = detail.RelatedEntityType,
            RelatedEntityId = detail.RelatedEntityId,
            ReceivedAt = detail.ReceivedAt,
            ProcessedAt = detail.ProcessedAt,
            ErrorMessage = detail.ErrorMessage,
            RetryCount = detail.RetryCount,
            IsAcknowledged = detail.IsAcknowledged,
            PayloadSizeBytes = detail.PayloadSizeBytes,
        };
    }
}
