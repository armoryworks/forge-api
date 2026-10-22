using FluentValidation;
using MediatR;

using Forge.Api.Validation;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record UpdateOperationCommand(int PartId, int OperationId, UpdateOperationRequestModel Data) : IRequest<OperationResponseModel>;

public class UpdateOperationValidator : AbstractValidator<UpdateOperationCommand>
{
    public UpdateOperationValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.OperationId).GreaterThan(0);
        RuleFor(x => x.Data.StepNumber).GreaterThan(0).When(x => x.Data.StepNumber.HasValue);
        RuleFor(x => x.Data.Title).NotEmpty().MaximumLength(200).When(x => x.Data.Title is not null);
        RuleFor(x => x.Data.Instructions).MaximumLength(4000).When(x => x.Data.Instructions is not null);
        RuleFor(x => x.Data.QcCriteria).MaximumLength(1000).When(x => x.Data.QcCriteria is not null);
        RuleFor(x => x.Data.SetupMinutes).GreaterThanOrEqualTo(0m).FitsNumeric(10, 2).When(x => x.Data.SetupMinutes.HasValue);
        RuleFor(x => x.Data.RunMinutesLot).GreaterThanOrEqualTo(0m).FitsNumeric(10, 2).When(x => x.Data.RunMinutesLot.HasValue);
        RuleFor(x => x.Data.EstimatedMs).InclusiveBetween(0L, NumericColumnRules.MaxOperationEstimatedMs).When(x => x.Data.EstimatedMs.HasValue);
        RuleFor(x => x.Data.ScrapFactor).GreaterThanOrEqualTo(0m).LessThan(1m).When(x => x.Data.ScrapFactor.HasValue);
        RuleFor(x => x.Data.RunMinutesEach).GreaterThanOrEqualTo(0m).FitsNumeric(10, 2).When(x => x.Data.RunMinutesEach.HasValue);
        RuleFor(x => x.Data.LaborAttendancePercent).InclusiveBetween(0m, 100m).FitsNumeric(5, 2).When(x => x.Data.LaborAttendancePercent.HasValue);

        // Phase 3 H5 / WU-13 — when toggling op TO subcontract, both fields
        // must be supplied in the same patch. (Resulting-state check in
        // the handler covers transitions where IsSubcontract is unchanged.)
        When(x => x.Data.IsSubcontract == true, () =>
        {
            RuleFor(x => x.Data.SubcontractVendorId)
                .NotNull()
                .GreaterThan(0)
                .WithMessage("Subcontract operations require a vendor.");
            RuleFor(x => x.Data.SubcontractTurnTimeDays)
                .NotNull()
                .GreaterThan(0m)
                .WithMessage("Subcontract operations require a positive turn time (days).")
                .FitsNumeric(5, 1);
        });
    }
}

public class UpdateOperationHandler(IPartRepository repo, IVendorRepository vendorRepo)
    : IRequestHandler<UpdateOperationCommand, OperationResponseModel>
{
    public async Task<OperationResponseModel> Handle(UpdateOperationCommand request, CancellationToken cancellationToken)
    {
        var operation = await repo.FindOperationAsync(request.OperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.OperationId} not found");

        if (operation.PartId != request.PartId)
            throw new KeyNotFoundException($"Operation {request.OperationId} does not belong to part {request.PartId}");

        var data = request.Data;

        if (data.StepNumber.HasValue) operation.StepNumber = data.StepNumber.Value;
        if (data.Title is not null) operation.Title = data.Title.Trim();
        if (data.Instructions is not null) operation.Instructions = data.Instructions.Trim();
        if (data.WorkCenterId is not null) operation.WorkCenterId = data.WorkCenterId;
        if (data.EstimatedMs is not null) operation.EstimatedMs = data.EstimatedMs;
        if (data.SetupMinutes.HasValue) operation.SetupMinutes = data.SetupMinutes.Value;
        if (data.RunMinutesLot.HasValue) operation.RunMinutesLot = data.RunMinutesLot.Value;
        if (data.RunMinutesEach.HasValue) operation.RunMinutesEach = data.RunMinutesEach.Value;
        if (data.LaborAttendancePercent.HasValue) operation.LaborAttendancePercent = data.LaborAttendancePercent.Value;
        if (data.ScrapFactor.HasValue) operation.ScrapFactor = Math.Round(data.ScrapFactor.Value, 4, MidpointRounding.ToZero);
        if (data.IsQcCheckpoint.HasValue) operation.IsQcCheckpoint = data.IsQcCheckpoint.Value;
        if (data.QcCriteria is not null) operation.QcCriteria = data.QcCriteria.Trim();
        if (data.ReferencedOperationId is not null) operation.ReferencedOperationId = data.ReferencedOperationId == 0 ? null : data.ReferencedOperationId;

        // Phase 3 H5 / WU-13 — apply subcontract metadata. Patch semantics:
        // explicit IsSubcontract=true sets the flag (and required fields);
        // explicit IsSubcontract=false flips it off and clears the vendor
        // / turn-time so a re-flag later doesn't inherit stale data; null
        // leaves the existing flag alone (existing patch convention).
        if (data.IsSubcontract.HasValue)
        {
            operation.IsSubcontract = data.IsSubcontract.Value;
            if (data.IsSubcontract.Value)
            {
                operation.SubcontractVendorId = data.SubcontractVendorId;
                operation.SubcontractTurnTimeDays = data.SubcontractTurnTimeDays;
            }
            else
            {
                operation.SubcontractVendorId = null;
                operation.SubcontractTurnTimeDays = null;
            }
        }
        else
        {
            // Subcontract flag unchanged; allow updating individual fields.
            if (data.SubcontractVendorId is not null) operation.SubcontractVendorId = data.SubcontractVendorId == 0 ? null : data.SubcontractVendorId;
            if (data.SubcontractTurnTimeDays is not null) operation.SubcontractTurnTimeDays = data.SubcontractTurnTimeDays;
        }

        // Resulting-state check: if the op ends up as a subcontract, both
        // fields must be present and the vendor must be active.
        if (operation.IsSubcontract)
        {
            if (operation.SubcontractVendorId is null || operation.SubcontractTurnTimeDays is null || operation.SubcontractTurnTimeDays <= 0)
            {
                throw new FluentValidation.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        operation.SubcontractVendorId is null ? "subcontractVendorId" : "subcontractTurnTimeDays",
                        "Subcontract operations require a vendor and a positive turn time (days)."),
                });
            }

            var vendor = await vendorRepo.FindAsync(operation.SubcontractVendorId.Value, cancellationToken);
            ActiveCheck.EnsureActive(vendor, "Vendor", "subcontractVendorId", operation.SubcontractVendorId.Value);
        }

        await repo.SaveChangesAsync(cancellationToken);

        var operations = await repo.GetOperationsAsync(request.PartId, cancellationToken);
        return operations.First(s => s.Id == operation.Id);
    }
}
