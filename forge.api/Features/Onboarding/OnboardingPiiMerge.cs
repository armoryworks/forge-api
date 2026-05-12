using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Bridges the server-side encrypted draft (EmployeeProfile.SsnProtected,
/// BankRoutingProtected, BankAccountProtected) into the BuildFormDataDictionary
/// output. When the wizard submits with a sensitive field intentionally blank
/// (because the user already entered it earlier and the field rendered with
/// the "Securely stored" indicator), we decrypt the persisted ciphertext and
/// inject the plaintext into the dictionary so PDF fill / DocuSeal still see
/// the right value.
///
/// Pure helper — call it AFTER SubmitOnboardingHandler.BuildFormDataDictionary
/// and BEFORE serializing the dictionary to JSON.
/// </summary>
public static class OnboardingPiiMerge
{
    public static async Task MergeStoredPiiAsync(
        AppDbContext db, IPiiProtector pii, int userId,
        Dictionary<string, string> formData, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null) return;

        // SSN — also fix up the dashed variant since AcroFieldMapJson templates
        // often reference one or the other.
        if (IsBlank(formData, "ssn") && !string.IsNullOrEmpty(profile.SsnProtected))
        {
            var ssn = pii.Unprotect(profile.SsnProtected);
            if (!string.IsNullOrEmpty(ssn))
            {
                formData["ssn"] = ssn;
                formData["ssnDash"] = SubmitOnboardingHandler.FormatSsn(ssn);
            }
        }

        if (IsBlank(formData, "routingNumber") && !string.IsNullOrEmpty(profile.BankRoutingProtected))
        {
            var routing = pii.Unprotect(profile.BankRoutingProtected);
            if (!string.IsNullOrEmpty(routing))
                formData["routingNumber"] = routing;
        }

        if (IsBlank(formData, "accountNumber") && !string.IsNullOrEmpty(profile.BankAccountProtected))
        {
            var account = pii.Unprotect(profile.BankAccountProtected);
            if (!string.IsNullOrEmpty(account))
                formData["accountNumber"] = account;
        }

        // I-9 Section 1 — alien-reg / I-94 / foreign-passport. Same shape as SSN:
        // wizard sends blank when the user keeps the previously stored value.
        if (IsBlank(formData, "i9AlienRegNumber") && !string.IsNullOrEmpty(profile.I9AlienRegProtected))
        {
            var alien = pii.Unprotect(profile.I9AlienRegProtected);
            if (!string.IsNullOrEmpty(alien))
                formData["i9AlienRegNumber"] = alien;
        }
        if (IsBlank(formData, "i9I94Number") && !string.IsNullOrEmpty(profile.I9I94Protected))
        {
            var i94 = pii.Unprotect(profile.I9I94Protected);
            if (!string.IsNullOrEmpty(i94))
                formData["i9I94Number"] = i94;
        }
        if (IsBlank(formData, "i9ForeignPassportNumber") && !string.IsNullOrEmpty(profile.I9ForeignPassportProtected))
        {
            var fp = pii.Unprotect(profile.I9ForeignPassportProtected);
            if (!string.IsNullOrEmpty(fp))
                formData["i9ForeignPassportNumber"] = fp;
        }
    }

    private static bool IsBlank(Dictionary<string, string> d, string key) =>
        !d.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v);
}
