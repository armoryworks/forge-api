using QBEngineer.Core.Enums;

namespace QBEngineer.Core.Entities;

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
}

