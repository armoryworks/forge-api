
namespace Forge.Core.Entities;

public class EmployeeProfile : BaseAuditableEntity
{
    // Phase 3 / WU-19 / F9: nullable so an Employee can exist with no User
    // account (HR onboards before IT provisions access).
    public int? UserId { get; set; }

    // Identity (denormalized when no User account exists; User overrides
    // these when present at projection time)
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? WorkEmail { get; set; }

    // Personal
    public DateTimeOffset? DateOfBirth { get; set; }
    public string? Gender { get; set; }

    // Address
    public string? Street1 { get; set; }
    public string? Street2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }

    // Contact
    public string? PhoneNumber { get; set; }
    public string? PersonalEmail { get; set; }

    // Emergency Contact
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }

    // Employment (admin-editable)
    public DateTimeOffset? StartDate { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? EmployeeNumber { get; set; }
    public PayType? PayType { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? SalaryAmount { get; set; }

    // Tax/Compliance (completion tracking — dates only, no actual tax data)
    public DateTimeOffset? W4CompletedAt { get; set; }
    public DateTimeOffset? StateWithholdingCompletedAt { get; set; }
    public DateTimeOffset? I9CompletedAt { get; set; }
    public DateTimeOffset? I9ExpirationDate { get; set; }
    public DateTimeOffset? DirectDepositCompletedAt { get; set; }
    public DateTimeOffset? WorkersCompAcknowledgedAt { get; set; }
    public DateTimeOffset? HandbookAcknowledgedAt { get; set; }

    // Set when user self-certifies onboarding complete without going through the wizard
    public DateTimeOffset? OnboardingBypassedAt { get; set; }

    // ── Sensitive identifiers (ASP.NET Data Protection ciphertext) ─────────
    // These columns store ciphertext only — never readable as plaintext from
    // the DB without the active DP key chain. The application reads them
    // through IPiiProtector at the seams that need plaintext (PDF fill /
    // DocuSeal submission). Never project these to a client-facing response
    // model. NULL means "not yet entered"; the UI uses presence to render
    // the "Securely stored — re-enter to overwrite" indicator.
    public string? SsnProtected { get; set; }
    public string? BankName { get; set; }
    public string? BankRoutingProtected { get; set; }
    public string? BankAccountProtected { get; set; }
    public string? BankAccountType { get; set; }

    // ── W-4 Federal Tax Withholding (not sensitive — plaintext OK) ─────────
    // Filing status, dependent counts, dollar-amount adjustments. Persisted
    // here so the user doesn't have to re-enter when they revisit step 3.
    public string? W4FilingStatus { get; set; }
    public bool? W4MultipleJobs { get; set; }
    public int? W4QualifyingChildren { get; set; }
    public int? W4OtherDependents { get; set; }
    public decimal? W4OtherIncome { get; set; }
    public decimal? W4Deductions { get; set; }
    public decimal? W4ExtraWithholding { get; set; }
    public bool? W4ExemptFromWithholding { get; set; }

    // ── State Tax Withholding (not sensitive — plaintext OK) ───────────────
    public string? StateFilingStatus { get; set; }
    public int? StateAllowances { get; set; }
    public decimal? StateAdditionalWithholding { get; set; }
    public bool? StateExempt { get; set; }

    // ── I-9 Section 1 — citizenship / immigration ──────────────────────────
    // Citizenship status code (1/2/3/4) is not sensitive on its own.
    // Alien-reg #, I-94 #, and foreign-passport # uniquely identify the
    // person for federal verification — encrypted via IPiiProtector. Country
    // + work-auth expiry are fine plaintext.
    public string? I9CitizenshipStatus { get; set; }
    public string? I9AlienRegProtected { get; set; }
    public string? I9I94Protected { get; set; }
    public string? I9ForeignPassportProtected { get; set; }
    public string? I9ForeignPassportCountry { get; set; }
    public DateTimeOffset? I9WorkAuthExpiry { get; set; }
}

