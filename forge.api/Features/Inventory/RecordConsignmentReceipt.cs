using FluentValidation;
using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record RecordConsignmentReceiptCommand(int AgreementId, RecordConsignmentTransactionRequestModel Request, int UserId = 0) : IRequest<ConsignmentTransactionResponseModel>;

public class RecordConsignmentReceiptValidator : AbstractValidator<RecordConsignmentReceiptCommand>
{
    public RecordConsignmentReceiptValidator()
    {
        RuleFor(x => x.Request.Quantity).GreaterThan(0m);
        RuleFor(x => x.Request.LotNumber).MaximumLength(100);
        RuleFor(x => x.Request.Notes).MaximumLength(2000);
    }
}

public class RecordConsignmentReceiptHandler(IConsignmentStockService consignment) : IRequestHandler<RecordConsignmentReceiptCommand, ConsignmentTransactionResponseModel>
{
    public Task<ConsignmentTransactionResponseModel> Handle(RecordConsignmentReceiptCommand command, CancellationToken cancellationToken)
        => consignment.ReceiveAsync(command.AgreementId, command.Request, command.UserId, cancellationToken);
}
