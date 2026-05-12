using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Api.Features.Capabilities.Validate;

/// <summary>
/// Phase 4 Phase-E — Validate-only ("dry run") variant of bulk-toggle. Returns
/// the same constraint-violation envelope the bulk-toggle would, but does not
/// persist anything or refresh the snapshot. Useful for client-side preview
/// before committing a multi-toggle change (e.g. the Phase G preset-apply
/// confirmation modal which lists the violations the user would hit before
/// they click Apply).
///
/// Not gated by any capability; admin-only via the controller's
/// [Authorize(Roles = "Admin")] guard.
/// </summary>
public record ValidateCapabilityChangesCommand(IReadOnlyList<ValidateChangeItem> Items)
    : IRequest<ValidateCapabilityChangesResponseModel>;

public record ValidateChangeItem(string Id, bool Enabled);

public record ValidateCapabilityChangesResponseModel(
    bool Valid,
    IReadOnlyList<CapabilityValidationViolation> Violations);

public record CapabilityValidationViolation(
    string Code,
    string Capability,
    string Message,
    IReadOnlyList<string>? Missing = null,
    IReadOnlyList<string>? Conflicts = null,
    IReadOnlyList<string>? Dependents = null);

public class ValidateCapabilityChangesValidator : AbstractValidator<ValidateCapabilityChangesCommand>
{
    public ValidateCapabilityChangesValidator()
    {
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty()
            .Must(items => items.Select(i => i.Id).Distinct().Count() == items.Count)
            .WithMessage("Duplicate capability IDs in validation payload.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).NotEmpty().Matches("^CAP-[A-Z0-9-]+$");
        });
    }
}

public class ValidateCapabilityChangesHandler(
    AppDbContext db,
    ICapabilitySnapshotProvider snapshots)
    : IRequestHandler<ValidateCapabilityChangesCommand, ValidateCapabilityChangesResponseModel>
{
    public async Task<ValidateCapabilityChangesResponseModel> Handle(
        ValidateCapabilityChangesCommand request,
        CancellationToken cancellationToken)
    {
        var ids = request.Items.Select(i => i.Id).ToList();

        // Verify every code is known. Unknown codes are themselves a violation
        // — surface them immediately rather than silently dropping.
        var rows = await db.Capabilities
            .AsNoTracking()
            .Where(c => ids.Contains(c.Code))
            .Select(c => new { c.Code, c.Enabled })
            .ToListAsync(cancellationToken);

        var rowByCode = rows.ToDictionary(r => r.Code, StringComparer.Ordinal);
        var violations = new List<CapabilityValidationViolation>();

        foreach (var id in ids)
        {
            if (!rowByCode.ContainsKey(id))
            {
                violations.Add(new CapabilityValidationViolation(
                    Code: "capability-not-found",
                    Capability: id,
                    Message: $"Unknown capability code '{id}'."));
            }
        }

        // Build the candidate state map: start from the current snapshot,
        // overlay the proposed delta. This mirrors the bulk-toggle handler's
        // whole-set semantic (Phase C decision D4).
        var candidate = new Dictionary<string, bool>(
            snapshots.Current.EnabledByCode,
            StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            if (rowByCode.ContainsKey(item.Id))
                candidate[item.Id] = item.Enabled;
        }

        // Evaluate each item that's actually changing. Idempotent rows are
        // skipped (matches BulkToggleCapabilitiesHandler).
        foreach (var item in request.Items)
        {
            if (!rowByCode.TryGetValue(item.Id, out var row)) continue;
            if (row.Enabled == item.Enabled) continue;

            if (item.Enabled)
            {
                var missing = CapabilityDependencyResolver.FindMissingDependencies(item.Id, candidate);
                if (missing.Count > 0)
                {
                    violations.Add(new CapabilityValidationViolation(
                        Code: "capability-missing-dependencies",
                        Capability: item.Id,
                        Message: $"'{item.Id}' requires: {string.Join(", ", missing)}",
                        Missing: missing));
                }
                var conflicts = CapabilityDependencyResolver.FindEnabledMutexConflicts(item.Id, candidate);
                if (conflicts.Count > 0)
                {
                    violations.Add(new CapabilityValidationViolation(
                        Code: "capability-mutex-violation",
                        Capability: item.Id,
                        Message: $"'{item.Id}' conflicts with enabled: {string.Join(", ", conflicts)}",
                        Conflicts: conflicts));
                }
            }
            else
            {
                var dependents = CapabilityDependencyResolver.FindEnabledDependents(item.Id, candidate);
                if (dependents.Count > 0)
                {
                    violations.Add(new CapabilityValidationViolation(
                        Code: "capability-has-dependents",
                        Capability: item.Id,
                        Message: $"'{item.Id}' is required by: {string.Join(", ", dependents)}",
                        Dependents: dependents));
                }
            }
        }

        return new ValidateCapabilityChangesResponseModel(
            Valid: violations.Count == 0,
            Violations: violations);
    }
}
