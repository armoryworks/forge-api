using FluentValidation;
using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record ReconcileConsignmentCommand(int AgreementId, ReconcileConsignmentRequestModel Request, int UserId = 0) : IRequest<ConsignmentReconciliationResponseModel>;

public class ReconcileConsignmentValidator : AbstractValidator<ReconcileConsignmentCommand>
{
    public ReconcileConsignmentValidator()
    {
        RuleFor(x => x.Request.PhysicalCount).GreaterThanOrEqualTo(0m).When(x => x.Request.PhysicalCount.HasValue);
        RuleFor(x => x.Request.PeriodTo).GreaterThanOrEqualTo(x => x.Request.PeriodFrom)
            .When(x => x.Request.PeriodFrom.HasValue && x.Request.PeriodTo.HasValue);
        RuleFor(x => x.Request.VendorInvoiceNumber).MaximumLength(60);
    }
}

public class ReconcileConsignmentHandler(IConsignmentStockService consignment) : IRequestHandler<ReconcileConsignmentCommand, ConsignmentReconciliationResponseModel>
{
    public Task<ConsignmentReconciliationResponseModel> Handle(ReconcileConsignmentCommand command, CancellationToken cancellationToken)
        => consignment.ReconcileAsync(command.AgreementId, command.Request, command.UserId, cancellationToken);
}
