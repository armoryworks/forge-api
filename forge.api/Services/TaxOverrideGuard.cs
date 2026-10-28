using System.Text.Json;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.SalesTax;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Gates manual tax-rate overrides on quotes, sales orders and invoices. A document's tax rate may
/// deviate from the rate computed for its customer (<see cref="GetTaxRateForCustomerQuery"/>) only
/// when the caller holds one of <see cref="OverrideRoles"/> and gives a <see cref="TaxOverrideReason"/>;
/// <see cref="TaxOverrideReason.Other"/> also needs a note, and the certificate-backed reasons need a
/// Verified, unexpired <see cref="CustomerTaxDocument"/> on file. Every accepted override is written
/// to the audit log. Registered as a concrete scoped service (same convention as
/// <see cref="CustomerPriceResolver"/>).
/// </summary>
public class TaxOverrideGuard(AppDbContext db, IMediator mediator, IClock clock, IHttpContextAccessor? httpContext = null)
{
    /// <summary>Error code when the caller's role may not override a tax rate (HTTP 403).</summary>
    public const string ForbiddenCode = "tax-override-forbidden";

    /// <summary>Error code when an overridden rate carries no reason (HTTP 400).</summary>
    public const string ReasonRequiredCode = "tax-override-reason-required";

    /// <summary>Error code when the reason is <see cref="TaxOverrideReason.Other"/> and no note is given (HTTP 400).</summary>
    public const string NoteRequiredCode = "tax-override-note-required";

    /// <summary>Error code when a certificate-backed reason has no verified, unexpired certificate on file (HTTP 400).</summary>
    public const string CertificateRequiredCode = "tax-override-certificate-required";

    /// <summary>Longest note an override may carry.</summary>
    public const int NoteMaxLength = 500;

    /// <summary>
    /// Roles that may override a document's tax rate: the roles that set a customer's tax exemption
    /// (<c>CustomersController.CommercialTermsRoles</c>) plus the Controller.
    /// </summary>
    public static IReadOnlyList<string> OverrideRoles { get; } = ["Admin", "Manager", "OfficeManager", "Controller"];

    /// <summary>Reasons that rest on an exemption certificate the customer must have on file.</summary>
    public static IReadOnlySet<TaxOverrideReason> CertificateReasons { get; } = new HashSet<TaxOverrideReason>
    {
        TaxOverrideReason.Resale,
        TaxOverrideReason.ExemptOrganization,
        TaxOverrideReason.Government,
    };

    /// <summary>
    /// The computed default rate for a customer — the same source the UI's
    /// auto-fill uses (`adminService.getTaxRateForCustomer` →
    /// <see cref="GetTaxRateForCustomerHandler"/>). 0 when no rate is configured.
    /// </summary>
    public async Task<decimal> GetDefaultRateAsync(int customerId, CancellationToken ct)
    {
        var rate = await mediator.Send(new GetTaxRateForCustomerQuery(customerId), ct);
        return rate?.Rate ?? 0m;
    }

    /// <summary>True when <paramref name="requestedRate"/> differs from <paramref name="computedDefaultRate"/>.</summary>
    public static bool IsOverride(decimal requestedRate, decimal computedDefaultRate)
        => decimal.Round(requestedRate, 6) != decimal.Round(computedDefaultRate, 6);

    /// <summary>True when the signed-in caller may override a tax rate.</summary>
    public bool CallerMayOverride()
        => httpContext?.HttpContext?.User is { } user && OverrideRoles.Any(user.IsInRole);

    /// <summary>
    /// Ensures the requested tax rate is allowed on a <paramref name="documentType"/> for the customer.
    /// When it matches the computed default, returns null (no override). When it deviates, checks the
    /// caller's role, the reason, the note and (for a certificate-backed reason) the certificate, records
    /// a "<c>{documentType}.tax_override</c>" audit row (flushed by the caller's SaveChangesAsync) and
    /// returns the override to stamp on the document.
    /// </summary>
    /// <exception cref="ForbiddenException">The caller's role may not override a tax rate.</exception>
    /// <exception cref="ValidationException">The reason, its note or its certificate is missing.</exception>
    public async Task<TaxOverrideStamp?> EnsureCanOverrideAsync(
        int customerId, decimal requestedRate, decimal computedDefaultRate,
        TaxOverrideReason? reason, string? note, string documentType, CancellationToken ct)
    {
        if (!IsOverride(requestedRate, computedDefaultRate))
            return null;

        if (!CallerMayOverride())
            throw new ForbiddenException(
                "Only an Admin, Manager, Office Manager or Controller can change the tax rate from the customer's rate.",
                ForbiddenCode);

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (reason is null || !Enum.IsDefined(reason.Value))
            throw Failure("TaxOverrideReason", ReasonRequiredCode,
                "Choose an exemption reason when the tax rate differs from the customer's rate.");
        if (trimmedNote is { Length: > NoteMaxLength })
            throw Failure("TaxOverrideNote", "MaximumLengthValidator",
                $"The tax override note can be at most {NoteMaxLength} characters.");
        if (reason == TaxOverrideReason.Other && trimmedNote is null)
            throw Failure("TaxOverrideNote", NoteRequiredCode,
                "Add a note explaining the tax override when the reason is Other.");

        int? documentId = null;
        if (CertificateReasons.Contains(reason.Value))
        {
            var now = clock.UtcNow;
            documentId = await db.CustomerTaxDocuments.AsNoTracking()
                .Where(d => d.CustomerId == customerId
                    && d.Status == TaxDocumentStatus.Verified
                    && (d.ExpirationDate == null || d.ExpirationDate > now))
                .OrderByDescending(d => d.VerifiedAt)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync(ct);

            if (documentId is null)
                throw Failure("TaxOverrideReason", CertificateRequiredCode,
                    "This exemption reason needs a verified tax exemption certificate on file for the customer.");
        }

        var stamp = new TaxOverrideStamp(reason.Value, trimmedNote, documentId);
        Audit(customerId, documentType, computedDefaultRate, requestedRate, stamp);
        return stamp;
    }

    /// <summary>
    /// Carries an override that was already accepted on a source document (a revision, an order made
    /// from a quote) onto a copy: the caller must still hold an override role when the copied rate
    /// deviates, and the copy is audited, but the source's reason is not asked for again.
    /// </summary>
    /// <exception cref="ForbiddenException">The copied rate deviates and the caller's role may not override a tax rate.</exception>
    public TaxOverrideStamp? EnsureCanCopyOverride(
        int customerId, decimal copiedRate, decimal computedDefaultRate,
        TaxOverrideReason? reason, string? note, int? taxDocumentId, string documentType)
    {
        if (!IsOverride(copiedRate, computedDefaultRate))
            return null;

        if (!CallerMayOverride())
            throw new ForbiddenException(
                "Only an Admin, Manager, Office Manager or Controller can copy a document whose tax rate differs from the customer's rate.",
                ForbiddenCode);

        var stamp = reason is null ? null : new TaxOverrideStamp(reason.Value, note, taxDocumentId);
        Audit(customerId, documentType, computedDefaultRate, copiedRate, stamp);
        return stamp;
    }

    private void Audit(int customerId, string documentType, decimal oldRate, decimal newRate, TaxOverrideStamp? stamp)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = db.CurrentUserId ?? 0,
            Action = $"{documentType}.tax_override",
            EntityType = "Customer",
            EntityId = customerId,
            Details = JsonSerializer.Serialize(new
            {
                oldRate,
                newRate,
                reason = stamp?.Reason.ToString(),
                note = stamp?.Note,
                taxDocumentId = stamp?.TaxDocumentId,
            }),
            CreatedAt = clock.UtcNow,
        });
    }

    private static ValidationException Failure(string property, string code, string message)
        => new(message, [new ValidationFailure(property, message) { ErrorCode = code }]);
}
