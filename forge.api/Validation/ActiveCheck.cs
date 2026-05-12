using System.Diagnostics.CodeAnalysis;

using FluentValidation;
using FluentValidation.Results;
using Forge.Core.Interfaces;

namespace Forge.Api.Validation;

/// <summary>
/// Shared active-check guard for the master-data → transaction edge.
///
/// Phase 1 found that a deactivated vendor still accepted new POs — the
/// <c>isActive</c> flag persisted, the UI showed it as inactive, but the PO
/// create handler had no validation gate. The same anti-pattern was suspected
/// across customer/part/asset/user transaction-create paths. Rather than
/// scatter per-handler boolean tests, transaction handlers call into this
/// helper, which throws a <see cref="ValidationException"/> that the
/// existing exception middleware turns into a 400 with the validation
/// envelope.
///
/// Usage:
/// <code>
///     await ActiveCheck.EnsureActiveAsync(vendor, "vendorId", ct);
/// </code>
///
/// (Phase 3 H2 / WU-12. Cases addressed: H2 (TODO), P3-PO-001 / P3-PO-002,
/// P2-VENDOR-001 / P2-VENDOR-002.)
/// </summary>
public static class ActiveCheck
{
    /// <summary>
    /// Throws <see cref="KeyNotFoundException"/> when <paramref name="entity"/>
    /// is null, or <see cref="ValidationException"/> when the entity exists
    /// but its lifecycle state disqualifies it from new transactions. The
    /// validation message names the deactivated record so the operator can
    /// identify it in the UI without an extra round trip.
    /// </summary>
    /// <param name="entity">The looked-up master-data record (or null).</param>
    /// <param name="entityKindLabel">Human-friendly label for the failure
    /// message (e.g. "Vendor", "Customer", "Part"). Used both in the
    /// not-found message and in the validation message.</param>
    /// <param name="fieldPath">Field name for the validation envelope
    /// (e.g. "vendorId", "lines[0].partId"). Used as the dictionary key in
    /// the eventual ValidationProblemDetails response so the UI can highlight
    /// the offending control.</param>
    /// <param name="entityId">Used when the entity itself is null, so the
    /// thrown <see cref="KeyNotFoundException"/> matches the existing error
    /// message format used across the codebase.</param>
    public static void EnsureActive<T>(
        [NotNull] T? entity,
        string entityKindLabel,
        string fieldPath,
        int entityId)
        where T : class, IActiveAware
    {
        if (entity is null)
            throw new KeyNotFoundException($"{entityKindLabel} {entityId} not found");

        if (!entity.IsActiveForNewTransactions)
        {
            // Throw a FluentValidation ValidationException so the existing
            // ExceptionHandlingMiddleware emits the standard 400 envelope
            // with the field path the UI uses to highlight the bad input.
            var failure = new ValidationFailure(
                fieldPath,
                $"{entityKindLabel} '{entity.GetDisplayName()}' is deactivated. Reactivate it or choose another.")
            {
                AttemptedValue = entityId,
                ErrorCode = "Inactive",
            };
            throw new ValidationException(new[] { failure });
        }
    }
}
