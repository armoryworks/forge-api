using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

using EmployeeProfileEntity = Forge.Core.Entities.EmployeeProfile;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Partial onboarding-draft save. Upserts only the non-null fields in the
/// request onto EmployeeProfile + IdentityDocument(s). Sensitive identifiers
/// (SSN, bank routing/account, I-9 doc numbers) are encrypted through
/// IPiiProtector before they touch the database; passing null preserves the
/// existing ciphertext so a user who left a sensitive field blank doesn't
/// wipe what they entered earlier.
///
/// Designed to be called from each step's Continue handler — the wizard
/// sends whatever fields are filled in for that step. Idempotent.
/// </summary>
public record SaveOnboardingDraftCommand(
    int UserId,
    SaveOnboardingDraftRequestModel Model) : IRequest<OnboardingDraftStatusModel>;

public class SaveOnboardingDraftHandler(AppDbContext db, IPiiProtector pii, ISystemAuditWriter auditWriter)
    : IRequestHandler<SaveOnboardingDraftCommand, OnboardingDraftStatusModel>
{
    public async Task<OnboardingDraftStatusModel> Handle(
        SaveOnboardingDraftCommand request, CancellationToken ct)
    {
        var m = request.Model;

        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);
        if (profile is null)
        {
            profile = new EmployeeProfileEntity { UserId = request.UserId };
            db.EmployeeProfiles.Add(profile);
        }

        // ── Step 1: Personal ────────────────────────────────────────────────
        if (m.FirstName    is not null)  profile.FirstName     = m.FirstName;
        if (m.LastName     is not null)  profile.LastName      = m.LastName;
        if (m.DateOfBirth.HasValue)      profile.DateOfBirth   = m.DateOfBirth;
        if (m.Email        is not null)  profile.PersonalEmail = m.Email;
        if (m.Phone        is not null)  profile.PhoneNumber   = m.Phone;
        if (!string.IsNullOrWhiteSpace(m.Ssn))
            profile.SsnProtected = pii.Protect(m.Ssn);

        // ── Step 2: Address ─────────────────────────────────────────────────
        if (m.Street1      is not null)  profile.Street1 = m.Street1;
        if (m.Street2      is not null)  profile.Street2 = m.Street2;
        if (m.City         is not null)  profile.City    = m.City;
        if (m.AddressState is not null)  profile.State   = m.AddressState;
        if (m.ZipCode      is not null)  profile.ZipCode = m.ZipCode;

        // ── Step 3: W-4 ─────────────────────────────────────────────────────
        if (m.W4FilingStatus       is not null) profile.W4FilingStatus       = m.W4FilingStatus;
        if (m.W4MultipleJobs.HasValue)          profile.W4MultipleJobs       = m.W4MultipleJobs;
        if (m.W4QualifyingChildren.HasValue)    profile.W4QualifyingChildren = m.W4QualifyingChildren;
        if (m.W4OtherDependents.HasValue)       profile.W4OtherDependents    = m.W4OtherDependents;
        if (m.W4OtherIncome.HasValue)           profile.W4OtherIncome        = m.W4OtherIncome;
        if (m.W4Deductions.HasValue)            profile.W4Deductions         = m.W4Deductions;
        if (m.W4ExtraWithholding.HasValue)      profile.W4ExtraWithholding   = m.W4ExtraWithholding;
        if (m.W4ExemptFromWithholding.HasValue) profile.W4ExemptFromWithholding = m.W4ExemptFromWithholding;

        // ── Step 4: State Withholding ───────────────────────────────────────
        if (m.StateFilingStatus       is not null)  profile.StateFilingStatus = m.StateFilingStatus;
        if (m.StateAllowances.HasValue)             profile.StateAllowances   = m.StateAllowances;
        if (m.StateAdditionalWithholding.HasValue)  profile.StateAdditionalWithholding = m.StateAdditionalWithholding;
        if (m.StateExempt.HasValue)                 profile.StateExempt       = m.StateExempt;

        // ── Step 5: I-9 Section 1 (citizenship + alien fields) ──────────────
        if (m.I9CitizenshipStatus      is not null) profile.I9CitizenshipStatus      = m.I9CitizenshipStatus;
        if (m.I9ForeignPassportCountry is not null) profile.I9ForeignPassportCountry = m.I9ForeignPassportCountry;
        if (m.I9WorkAuthExpiry.HasValue)            profile.I9WorkAuthExpiry         = m.I9WorkAuthExpiry;
        if (!string.IsNullOrWhiteSpace(m.I9AlienRegNumber))
            profile.I9AlienRegProtected = pii.Protect(m.I9AlienRegNumber);
        if (!string.IsNullOrWhiteSpace(m.I9I94Number))
            profile.I9I94Protected = pii.Protect(m.I9I94Number);
        if (!string.IsNullOrWhiteSpace(m.I9ForeignPassportNumber))
            profile.I9ForeignPassportProtected = pii.Protect(m.I9ForeignPassportNumber);

        // ── Step 6: Direct Deposit ──────────────────────────────────────────
        var directDepositChanges = profile.DirectDepositCompletedAt is null
            ? []
            : DirectDepositChanges(profile, m);
        if (m.BankName    is not null) profile.BankName        = m.BankName;
        if (m.AccountType is not null) profile.BankAccountType = m.AccountType;
        if (!string.IsNullOrWhiteSpace(m.RoutingNumber))
            profile.BankRoutingProtected = pii.Protect(m.RoutingNumber);
        if (!string.IsNullOrWhiteSpace(m.AccountNumber))
            profile.BankAccountProtected = pii.Protect(m.AccountNumber);

        if (directDepositChanges.Count > 0)
            await NotifyDirectDepositChangedAsync(request.UserId, profile, directDepositChanges, ct);

        await db.SaveChangesAsync(ct);

        if (directDepositChanges.Count > 0)
        {
            await auditWriter.WriteAsync("DirectDepositChanged", db.CurrentUserId ?? request.UserId,
                entityType: "EmployeeProfile",
                entityId: profile.Id,
                details: System.Text.Json.JsonSerializer.Serialize(new
                {
                    targetUserId = request.UserId,
                    changedFields = directDepositChanges,
                    accountLast4 = Last4(pii.Unprotect(profile.BankAccountProtected)),
                }),
                ct: ct);
        }

        // ── Step 5: I-9 identity documents ──────────────────────────────────
        // One row per (UserId, DocumentType). Upsert by lookup; null fields
        // preserve existing values, doc number encrypted via IPiiProtector.
        await UpsertIdentityDocAsync(request.UserId, IdentityDocumentType.ListA,
            m.I9ListAType, m.I9ListADocNumber, m.I9ListAAuthority, m.I9ListAExpiry,
            m.I9ListAFileAttachmentId, ct);
        await UpsertIdentityDocAsync(request.UserId, IdentityDocumentType.ListB,
            m.I9ListBType, m.I9ListBDocNumber, m.I9ListBAuthority, m.I9ListBExpiry,
            m.I9ListBFileAttachmentId, ct);
        await UpsertIdentityDocAsync(request.UserId, IdentityDocumentType.ListC,
            m.I9ListCType, m.I9ListCDocNumber, m.I9ListCAuthority, m.I9ListCExpiry,
            m.I9ListCFileAttachmentId, ct);

        return await GetOnboardingDraftStatusHandler.LoadStatusAsync(db, request.UserId, ct);
    }

    private List<string> DirectDepositChanges(EmployeeProfileEntity profile, SaveOnboardingDraftRequestModel m)
    {
        var changes = new List<string>();
        if (m.BankName is not null && m.BankName != profile.BankName)
            changes.Add("bankName");
        if (m.AccountType is not null && m.AccountType != profile.BankAccountType)
            changes.Add("accountType");
        if (!string.IsNullOrWhiteSpace(m.RoutingNumber) && m.RoutingNumber != pii.Unprotect(profile.BankRoutingProtected))
            changes.Add("routingNumber");
        if (!string.IsNullOrWhiteSpace(m.AccountNumber) && m.AccountNumber != pii.Unprotect(profile.BankAccountProtected))
            changes.Add("accountNumber");
        return changes;
    }

    private async Task NotifyDirectDepositChangedAsync(
        int userId, EmployeeProfileEntity profile, List<string> changes, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var name = user is null
            ? $"{profile.LastName}, {profile.FirstName}"
            : $"{user.LastName}, {user.FirstName}";
        var accountChanged = changes.Contains("routingNumber") || changes.Contains("accountNumber");

        var payrollUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => (x.Name == "Admin" || x.Name == "OfficeManager")
                && x.UserId != userId
                && db.Users.Any(u => u.Id == x.UserId && u.IsActive))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var recipientId in payrollUserIds)
        {
            db.Notifications.Add(new Notification
            {
                Type = "direct_deposit_changed",
                Severity = "warning",
                Source = "payroll",
                Title = "Direct deposit changed",
                Message = accountChanged
                    ? $"{name} changed their direct deposit bank account. Confirm the change with them before the next payroll."
                    : $"{name} changed their direct deposit details.",
                EntityType = "employee_profiles",
                EntityId = profile.Id,
                UserId = recipientId,
            });
        }

        db.Notifications.Add(new Notification
        {
            Type = "direct_deposit_changed",
            Severity = "warning",
            Source = "payroll",
            Title = "Your direct deposit was changed",
            Message = "Your direct deposit details were changed. If you did not make this change, contact payroll right away.",
            EntityType = "employee_profiles",
            EntityId = profile.Id,
            UserId = userId,
        });
    }

    private static string? Last4(string? value)
        => string.IsNullOrEmpty(value) ? null : value.Length <= 4 ? value : value[^4..];

    private async Task UpsertIdentityDocAsync(
        int userId, IdentityDocumentType docType,
        string? docName, string? docNumber, string? authority,
        DateTimeOffset? expiry, int? fileAttachmentId,
        CancellationToken ct)
    {
        // No identifying fields supplied for this list → skip (don't create
        // an empty row).
        var anySupplied = docName is not null || docNumber is not null
            || authority is not null || expiry.HasValue || fileAttachmentId.HasValue;
        if (!anySupplied) return;

        var doc = await db.IdentityDocuments
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DocumentType == docType, ct);

        if (doc is null)
        {
            // File attachment is required by the entity (non-nullable int).
            // We only create when we have something to attach to — i.e. the
            // user uploaded a file. Otherwise hold off until they do.
            if (!fileAttachmentId.HasValue) return;
            doc = new IdentityDocument
            {
                UserId = userId,
                DocumentType = docType,
                FileAttachmentId = fileAttachmentId.Value,
            };
            db.IdentityDocuments.Add(doc);
        }
        else if (fileAttachmentId.HasValue)
        {
            doc.FileAttachmentId = fileAttachmentId.Value;
        }

        if (docName is not null)   doc.DocumentName     = docName;
        if (authority is not null) doc.IssuingAuthority = authority;
        if (expiry.HasValue)       doc.ExpiresAt        = expiry;
        if (!string.IsNullOrWhiteSpace(docNumber))
            doc.DocumentNumberProtected = pii.Protect(docNumber);

        await db.SaveChangesAsync(ct);
    }
}
