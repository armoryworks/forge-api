using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Customers;

public record UpdateCustomerCommand(
    int Id,
    string? Name,
    string? CompanyName,
    string? Email,
    string? Phone,
    bool? IsActive,
    bool? IsTaxExempt = null,
    string? TaxExemptionId = null,
    // Phase 1r / Batch 15-16 — regulated-industry flags + reference-customer
    // consent. Default null = "leave alone"; explicit true/false sets.
    bool? IsFdaRegulated = null,
    bool? IsAerospace = null,
    bool? IsAutomotive = null,
    bool? IsItarControlled = null,
    bool? IsReferenceOk = null,
    string? ReferenceNotes = null) : IRequest;

public class UpdateCustomerValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.CompanyName).MaximumLength(200).When(x => x.CompanyName is not null);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50).When(x => x.Phone is not null);
        RuleFor(x => x.TaxExemptionId).MaximumLength(50).When(x => x.TaxExemptionId is not null);
    }
}

public class UpdateCustomerHandler(ICustomerRepository repo, AppDbContext db, IClock clock)
    : IRequestHandler<UpdateCustomerCommand>
{
    public async Task Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.Id} not found");

        // Rollup rule — collect changed-field markers as we apply patches so
        // one activity row summarises the whole save instead of one per field.
        var changedFields = new List<string>();

        if (request.Name is not null && request.Name != customer.Name)
        {
            customer.Name = request.Name;
            changedFields.Add("name");
        }
        if (request.CompanyName is not null && request.CompanyName != customer.CompanyName)
        {
            customer.CompanyName = request.CompanyName;
            changedFields.Add("companyName");
        }
        if (request.Email is not null && request.Email != customer.Email)
        {
            customer.Email = request.Email;
            changedFields.Add("email");
        }
        if (request.Phone is not null && request.Phone != customer.Phone)
        {
            customer.Phone = request.Phone;
            changedFields.Add("phone");
        }

        // Phase 3 H2 / WU-12: stamp/clear DeactivationDate on lifecycle change.
        if (request.IsActive.HasValue && request.IsActive.Value != customer.IsActive)
        {
            customer.IsActive = request.IsActive.Value;
            customer.DeactivationDate = customer.IsActive ? null : clock.UtcNow;
            changedFields.Add(customer.IsActive ? "reactivated" : "deactivated");
        }

        if (request.IsTaxExempt.HasValue && request.IsTaxExempt.Value != customer.IsTaxExempt)
        {
            customer.IsTaxExempt = request.IsTaxExempt.Value;
            changedFields.Add("isTaxExempt");
        }
        if (request.TaxExemptionId is not null && request.TaxExemptionId != customer.TaxExemptionId)
        {
            customer.TaxExemptionId = request.TaxExemptionId;
            changedFields.Add("taxExemptionId");
        }

        // Phase 1r / Batch 15 — regulated-industry flags. Each is independent
        // (a customer can be both aerospace + ITAR-controlled, or automotive
        // alone). Toggle activity rows surface the new state by name.
        if (request.IsFdaRegulated.HasValue && request.IsFdaRegulated.Value != customer.IsFdaRegulated)
        {
            customer.IsFdaRegulated = request.IsFdaRegulated.Value;
            changedFields.Add($"isFdaRegulated: {customer.IsFdaRegulated}");
        }
        if (request.IsAerospace.HasValue && request.IsAerospace.Value != customer.IsAerospace)
        {
            customer.IsAerospace = request.IsAerospace.Value;
            changedFields.Add($"isAerospace: {customer.IsAerospace}");
        }
        if (request.IsAutomotive.HasValue && request.IsAutomotive.Value != customer.IsAutomotive)
        {
            customer.IsAutomotive = request.IsAutomotive.Value;
            changedFields.Add($"isAutomotive: {customer.IsAutomotive}");
        }
        if (request.IsItarControlled.HasValue && request.IsItarControlled.Value != customer.IsItarControlled)
        {
            customer.IsItarControlled = request.IsItarControlled.Value;
            changedFields.Add($"isItarControlled: {customer.IsItarControlled}");
        }
        // Phase 1r / Batch 16 — reference-customer consent + notes.
        if (request.IsReferenceOk.HasValue && request.IsReferenceOk.Value != customer.IsReferenceOk)
        {
            customer.IsReferenceOk = request.IsReferenceOk.Value;
            changedFields.Add($"isReferenceOk: {customer.IsReferenceOk}");
        }
        if (request.ReferenceNotes is not null && request.ReferenceNotes != customer.ReferenceNotes)
        {
            customer.ReferenceNotes = request.ReferenceNotes;
            changedFields.Add("referenceNotes");
        }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "updated",
                $"Updated {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
                ("Customer", customer.Id));
        }

        await repo.SaveChangesAsync(cancellationToken);
    }
}
