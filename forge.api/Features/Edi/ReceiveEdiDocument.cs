using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record ReceiveEdiDocumentCommand(ReceiveEdiDocumentRequestModel Model) : IRequest<EdiTransactionResponseModel>;

public class ReceiveEdiDocumentValidator : AbstractValidator<ReceiveEdiDocumentCommand>
{
    public ReceiveEdiDocumentValidator()
    {
        RuleFor(x => x.Model.RawPayload).NotEmpty();
        RuleFor(x => x.Model.TradingPartnerId).GreaterThan(0);
    }
}

public class ReceiveEdiDocumentHandler(AppDbContext db, IEdiService ediService, IMediator mediator)
    : IRequestHandler<ReceiveEdiDocumentCommand, EdiTransactionResponseModel>
{
    public async Task<EdiTransactionResponseModel> Handle(
        ReceiveEdiDocumentCommand request, CancellationToken cancellationToken)
    {
        var transaction = await ediService.ReceiveDocumentAsync(
            request.Model.RawPayload, request.Model.TradingPartnerId, cancellationToken);

        db.EdiTransactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

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
