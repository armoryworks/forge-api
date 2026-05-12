using Microsoft.EntityFrameworkCore;

using QBEngineer.Api.Services;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.Onboarding;

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
    }

    private static bool IsBlank(Dictionary<string, string> d, string key) =>
        !d.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v);
}
