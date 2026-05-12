using FluentValidation;
using MediatR;

using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record CreateOperationCommand(int PartId, CreateOperationRequestModel Data) : IRequest<OperationResponseModel>;

public class CreateOperationValidator : AbstractValidator<CreateOperationCommand>
{
    public CreateOperationValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Data.StepNumber).GreaterThan(0);
        RuleFor(x => x.Data.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Instructions).MaximumLength(4000).When(x => x.Data.Instructions is not null);
        RuleFor(x => x.Data.QcCriteria).MaximumLength(1000).When(x => x.Data.QcCriteria is not null);

        // Phase 3 H5 / WU-13 — when an operation is flagged as subcontract,
        // both vendor + turn time must be present. The active-check on the
        // referenced vendor lives in the handler (DB lookup required).
        When(x => x.Data.IsSubcontract == true, () =>
        {
            RuleFor(x => x.Data.SubcontractVendorId)
                .NotNull()
                .GreaterThan(0)
                .WithMessage("Subcontract operations require a vendor.");
            RuleFor(x => x.Data.SubcontractTurnTimeDays)
                .NotNull()
                .GreaterThan(0m)
                .WithMessage("Subcontract operations require a positive turn time (days).");
        });
    }
}

public class CreateOperationHandler(IPartRepository repo, IVendorRepository vendorRepo)
    : IRequestHandler<CreateOperationCommand, OperationResponseModel>
{
    public async Task<OperationResponseModel> Handle(CreateOperationCommand request, CancellationToken cancellationToken)
    {
        var part = await repo.FindAsync(request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var data = request.Data;
        var isSubcontract = data.IsSubcontract == true;

        // Phase 3 H5 / WU-13 — vendor active-check on subcontract ops, mirrors
        // the WU-12 master-data → transaction-edge pattern.
        if (isSubcontract && data.SubcontractVendorId is int vendorId)
        {
            var vendor = await vendorRepo.FindAsync(vendorId, cancellationToken);
            ActiveCheck.EnsureActive(vendor, "Vendor", "subcontractVendorId", vendorId);
        }

        var operation = new Operation
        {
            PartId = request.PartId,
            StepNumber = data.StepNumber,
            Title = data.Title.Trim(),
            Instructions = data.Instructions?.Trim(),
            WorkCenterId = data.WorkCenterId,
            EstimatedMinutes = data.EstimatedMinutes,
            IsQcCheckpoint = data.IsQcCheckpoint,
            QcCriteria = data.QcCriteria?.Trim(),
            ReferencedOperationId = data.ReferencedOperationId,
            // Phase 3 H5 / WU-13 — persist subcontract metadata.
            IsSubcontract = isSubcontract,
            SubcontractVendorId = isSubcontract ? data.SubcontractVendorId : null,
            SubcontractTurnTimeDays = isSubcontract ? data.SubcontractTurnTimeDays : null,
        };

        part.Operations.Add(operation);
        await repo.SaveChangesAsync(cancellationToken);

        var operations = await repo.GetOperationsAsync(request.PartId, cancellationToken);
        return operations.First(s => s.Id == operation.Id);
    }
}
