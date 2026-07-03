using Microsoft.AspNetCore.Identity;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Data.Context;

public class ApplicationUser : IdentityUser<int>, IActiveAware
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Initials { get; set; }
    public string? AvatarColor { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Setup token flow (admin creates user → employee completes account)
    public string? SetupToken { get; set; }
    public DateTimeOffset? SetupTokenExpiresAt { get; set; }

    // PIN for kiosk auth (Tier 2: barcode + PIN)
    public string? PinHash { get; set; }

    // Barcode for kiosk scan
    public string? EmployeeBarcode { get; set; }

    // Team assignment
    public int? TeamId { get; set; }

    // Work location (drives state withholding)
    public int? WorkLocationId { get; set; }
    public CompanyLocation? WorkLocation { get; set; }

    // Accounting integration (QB Employee ID for time activity sync)
    public string? AccountingEmployeeId { get; set; }

    // SSO identity linking
    public string? GoogleId { get; set; }
    public string? MicrosoftId { get; set; }
    public string? OidcSubjectId { get; set; }
    public string? OidcProvider { get; set; }

    // MFA
    public bool MfaEnabled { get; set; }
    public bool MfaEnforcedByPolicy { get; set; }
    public DateTimeOffset? MfaEnabledAt { get; set; }
    public int MfaRecoveryCodesRemaining { get; set; }

    // IActiveAware — Phase 3 H2 active-check. A deactivated user cannot be the
    // target of new shift-assignment / job-assignment records.
    public bool IsActiveForNewTransactions => IsActive;
    public string GetDisplayName()
    {
        var name = ($"{FirstName} {LastName}").Trim();
        return string.IsNullOrEmpty(name) ? (UserName ?? $"User #{Id}") : name;
    }
}
