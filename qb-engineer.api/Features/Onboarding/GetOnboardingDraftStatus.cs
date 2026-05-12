using MediatR;

using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Enums;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.Onboarding;

/// <summary>
/// Returns the current onboarding-draft state for the calling user. Non-
/// sensitive values are echoed verbatim; sensitive identifiers (SSN, bank
/// routing/account, I-9 doc numbers) are represented ONLY by their Has*
/// boolean — neither plaintext nor ciphertext is ever projected.
/// </summary>
public record GetOnboardingDraftStatusQuery(int UserId) : IRequest<OnboardingDraftStatusModel>;

public class GetOnboardingDraftStatusHandler(AppDbContext db)
    : IRequestHandler<GetOnboardingDraftStatusQuery, OnboardingDraftStatusModel>
{
    public Task<OnboardingDraftStatusModel> Handle(
        GetOnboardingDraftStatusQuery request, CancellationToken ct) =>
        LoadStatusAsync(db, request.UserId, ct);

    /// <summary>
    /// Shared loader used by SaveOnboardingDraftHandler so both endpoints
    /// return the same shape after a write.
    /// </summary>
    public static async Task<OnboardingDraftStatusModel> LoadStatusAsync(
        AppDbContext db, int userId, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var docs = await db.IdentityDocuments
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .ToDictionaryAsync(d => d.DocumentType, ct);

        docs.TryGetValue(IdentityDocumentType.ListA, out var listA);
        docs.TryGetValue(IdentityDocumentType.ListB, out var listB);
        docs.TryGetValue(IdentityDocumentType.ListC, out var listC);

        // Document choice is inferred from which list(s) have a row.
        string? docChoice = listA is not null ? "A"
            : (listB is not null || listC is not null) ? "BC"
            : null;

        return new OnboardingDraftStatusModel(
            FirstName:    profile?.FirstName,
            MiddleName:   null, // EmployeeProfile doesn't store middle name today
            LastName:     profile?.LastName,
            DateOfBirth:  profile?.DateOfBirth,
            Email:        profile?.PersonalEmail,
            Phone:        profile?.PhoneNumber,
            HasSsn:       !string.IsNullOrEmpty(profile?.SsnProtected),

            Street1:      profile?.Street1,
            Street2:      profile?.Street2,
            City:         profile?.City,
            AddressState: profile?.State,
            ZipCode:      profile?.ZipCode,

            I9DocumentChoice: docChoice,

            I9ListAType:               listA?.DocumentName,
            I9ListAAuthority:          listA?.IssuingAuthority,
            I9ListAExpiry:             listA?.ExpiresAt,
            I9ListAFileAttachmentId:   listA?.FileAttachmentId,
            HasListADocNumber:         !string.IsNullOrEmpty(listA?.DocumentNumberProtected),

            I9ListBType:               listB?.DocumentName,
            I9ListBAuthority:          listB?.IssuingAuthority,
            I9ListBExpiry:             listB?.ExpiresAt,
            I9ListBFileAttachmentId:   listB?.FileAttachmentId,
            HasListBDocNumber:         !string.IsNullOrEmpty(listB?.DocumentNumberProtected),

            I9ListCType:               listC?.DocumentName,
            I9ListCAuthority:          listC?.IssuingAuthority,
            I9ListCExpiry:             listC?.ExpiresAt,
            I9ListCFileAttachmentId:   listC?.FileAttachmentId,
            HasListCDocNumber:         !string.IsNullOrEmpty(listC?.DocumentNumberProtected),

            BankName:        profile?.BankName,
            AccountType:     profile?.BankAccountType,
            HasBankRouting:  !string.IsNullOrEmpty(profile?.BankRoutingProtected),
            HasBankAccount:  !string.IsNullOrEmpty(profile?.BankAccountProtected));
    }
}
