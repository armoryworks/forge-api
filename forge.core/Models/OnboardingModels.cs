namespace Forge.Core.Models;

/// <summary>
/// Unified request model for the new-hire onboarding wizard.
/// Shared fields (personal info, address) are collected once and used for ALL government forms.
/// Form-specific fields are grouped by step so the correct instructions can be displayed.
/// The backend maps all fields to AcroForm fields via each template's AcroFieldMapJson.
/// </summary>
public record OnboardingSubmitRequestModel(
    // ── Step 1: Personal Information (shared across all forms) ──────────
    string FirstName,
    string? MiddleName,
    string LastName,
    string? OtherLastNames,
    DateTimeOffset DateOfBirth,
    string Ssn,
    string Email,
    string Phone,

    // ── Step 2: Home Address (shared across all forms) ───────────────────
    string Street1,
    string? Street2,
    string City,
    string AddressState,
    string ZipCode,

    // ── Step 3: W-4 Federal Withholding (form-specific) ─────────────────
    string W4FilingStatus,
    bool W4MultipleJobs,
    decimal W4ClaimDependentsAmount,
    decimal W4OtherIncome,
    decimal W4Deductions,
    decimal W4ExtraWithholding,
    bool W4ExemptFromWithholding,

    // ── Step 4: State Withholding (form-specific) ────────────────────────
    string? StateFilingStatus,
    int? StateAllowances,
    decimal? StateAdditionalWithholding,
    bool? StateExempt,

    // ── Step 5: I-9 Employment Eligibility (form-specific) ───────────────
    /// <summary>1=US Citizen, 2=Noncitizen National, 3=LPR, 4=Alien Authorized to Work</summary>
    string I9CitizenshipStatus,
    string? I9AlienRegNumber,
    string? I9I94Number,
    string? I9ForeignPassportNumber,
    string? I9ForeignPassportCountry,
    DateTimeOffset? I9WorkAuthExpiry,
    bool I9PreparedByPreparer,
    string? I9PreparerFirstName,
    string? I9PreparerLastName,
    string? I9PreparerAddress,
    string? I9PreparerCity,
    string? I9PreparerState,
    string? I9PreparerZip,

    // ── I-9 Identity Documents ───────────────────────────────────────────
    /// <summary>"A" = presented a List A document; "BC" = presented List B + List C documents.</summary>
    string? I9DocumentChoice,
    string? I9ListAType,
    string? I9ListADocNumber,
    string? I9ListAAuthority,
    DateTimeOffset? I9ListAExpiry,
    int? I9ListAFileAttachmentId,
    string? I9ListBType,
    string? I9ListBDocNumber,
    string? I9ListBAuthority,
    DateTimeOffset? I9ListBExpiry,
    int? I9ListBFileAttachmentId,
    string? I9ListCType,
    string? I9ListCDocNumber,
    string? I9ListCAuthority,
    DateTimeOffset? I9ListCExpiry,
    int? I9ListCFileAttachmentId,

    // ── Step 6: Direct Deposit ───────────────────────────────────────────
    string BankName,
    string RoutingNumber,
    string AccountNumber,
    string AccountType,
    int? VoidedCheckFileAttachmentId,

    // ── Step 7: Acknowledgments ─────────────────────────────────────────
    bool AcknowledgeWorkersComp,
    bool AcknowledgeHandbook
);

public record OnboardingSigningUrlModel(
    string FormType,
    string FormName,
    string SigningUrl,
    int SubmissionId);

public record OnboardingSubmitResultModel(
    bool RequiresSigning,
    IReadOnlyList<OnboardingSigningUrlModel> SigningUrls,
    int? I9EmployerDocuSealSubmitterId);

public record OnboardingStatusModel(
    bool W4Complete,
    bool I9Complete,
    bool StateWithholdingComplete,
    bool DirectDepositComplete,
    bool WorkersCompComplete,
    bool HandbookComplete,
    bool AllComplete,
    bool CanBeAssigned);

// ── Per-form review flow ──────────────────────────────────────────────────────

/// <summary>Describes one form that the employee needs to review and sign.</summary>
public record OnboardingFormToSignItem(
    string FormType,
    string FormName,
    bool HasTemplate);

/// <summary>Result of the save-only step (before any PDF fill / DocuSeal).</summary>
public record SaveOnboardingResultModel(
    IReadOnlyList<OnboardingFormToSignItem> FormsToSign);

/// <summary>Request to preview a single filled PDF (no DocuSeal, no DB writes).</summary>
public record PreviewOnboardingPdfRequestModel(
    OnboardingSubmitRequestModel FormData,
    string FormType);

/// <summary>
/// Preview result. When <see cref="HasTemplate"/> is false the form has not been
/// configured for PDF pre-fill; the frontend should skip straight to signing.
/// </summary>
public record PreviewOnboardingPdfResultModel(
    bool HasTemplate,
    string? PdfBase64);

/// <summary>Request to fill one PDF and create a DocuSeal submission for it.</summary>
public record SignOnboardingFormRequestModel(
    OnboardingSubmitRequestModel FormData,
    string FormType);

public record SignOnboardingFormResultModel(
    string SigningUrl,
    int SubmissionId,
    bool IsMock);

/// <summary>
/// Policy documents surfaced on the Acknowledgments step. Empty / null URLs
/// mean the document isn't configured yet — the UI hides the corresponding
/// acknowledgment when its URL is blank.
/// </summary>
public record OnboardingPolicyDocsModel(
    string? WorkersCompDocUrl,
    string? HandbookDocUrl);

// ── Server-side draft persistence ─────────────────────────────────────────
//
// Per-step Save on the wizard writes whatever fields the user has filled in
// to the real tables (EmployeeProfile, IdentityDocument). Sensitive
// identifiers — SSN, bank routing/account, I-9 doc numbers — go through
// IPiiProtector and land in *_protected columns; they are NEVER echoed back
// in the GET status payload. Instead the status carries Has* flags so the
// wizard can render a "Securely stored — re-enter to overwrite" indicator
// next to fields that are intentionally blank for security reasons.

/// <summary>
/// Partial onboarding draft. Every field is optional — the handler upserts
/// the non-null subset. Sensitive fields are encrypted on write; passing
/// null preserves the existing ciphertext (the user hasn't re-entered).
/// </summary>
public record SaveOnboardingDraftRequestModel(
    // Step 1 — Personal
    string? FirstName,
    string? MiddleName,
    string? LastName,
    DateTimeOffset? DateOfBirth,
    string? Ssn,
    string? Email,
    string? Phone,

    // Step 2 — Address
    string? Street1,
    string? Street2,
    string? City,
    string? AddressState,
    string? ZipCode,

    // Step 3 — W-4 Federal Withholding
    string? W4FilingStatus,
    bool? W4MultipleJobs,
    int? W4QualifyingChildren,
    int? W4OtherDependents,
    decimal? W4OtherIncome,
    decimal? W4Deductions,
    decimal? W4ExtraWithholding,
    bool? W4ExemptFromWithholding,

    // Step 4 — State Tax Withholding
    string? StateFilingStatus,
    int? StateAllowances,
    decimal? StateAdditionalWithholding,
    bool? StateExempt,

    // Step 5 — I-9 (citizenship + identity-document details)
    string? I9CitizenshipStatus,
    string? I9AlienRegNumber,
    string? I9I94Number,
    string? I9ForeignPassportNumber,
    string? I9ForeignPassportCountry,
    DateTimeOffset? I9WorkAuthExpiry,
    string? I9DocumentChoice,
    string? I9ListAType,
    string? I9ListADocNumber,
    string? I9ListAAuthority,
    DateTimeOffset? I9ListAExpiry,
    int? I9ListAFileAttachmentId,
    string? I9ListBType,
    string? I9ListBDocNumber,
    string? I9ListBAuthority,
    DateTimeOffset? I9ListBExpiry,
    int? I9ListBFileAttachmentId,
    string? I9ListCType,
    string? I9ListCDocNumber,
    string? I9ListCAuthority,
    DateTimeOffset? I9ListCExpiry,
    int? I9ListCFileAttachmentId,

    // Step 6 — Direct Deposit
    string? BankName,
    string? RoutingNumber,
    string? AccountNumber,
    string? AccountType
);

/// <summary>
/// Echoed draft state. Sensitive fields are represented only by their Has*
/// boolean — never the plaintext or ciphertext. Used to repopulate the
/// wizard on reload + drive the "Securely stored" indicator.
/// </summary>
public record OnboardingDraftStatusModel(
    // Step 1
    string? FirstName,
    string? MiddleName,
    string? LastName,
    DateTimeOffset? DateOfBirth,
    string? Email,
    string? Phone,
    bool HasSsn,

    // Step 2
    string? Street1,
    string? Street2,
    string? City,
    string? AddressState,
    string? ZipCode,

    // Step 3 — W-4
    string? W4FilingStatus,
    bool? W4MultipleJobs,
    int? W4QualifyingChildren,
    int? W4OtherDependents,
    decimal? W4OtherIncome,
    decimal? W4Deductions,
    decimal? W4ExtraWithholding,
    bool? W4ExemptFromWithholding,

    // Step 4 — State Tax
    string? StateFilingStatus,
    int? StateAllowances,
    decimal? StateAdditionalWithholding,
    bool? StateExempt,

    // Step 5 — I-9 Section 1 (citizenship + alien-fields meta)
    string? I9CitizenshipStatus,
    bool HasAlienRegNumber,
    bool HasI94Number,
    bool HasForeignPassportNumber,
    string? I9ForeignPassportCountry,
    DateTimeOffset? I9WorkAuthExpiry,

    // Step 5 (I-9 doc detail; doc numbers represented only by Has*)
    string? I9DocumentChoice,
    string? I9ListAType,
    string? I9ListAAuthority,
    DateTimeOffset? I9ListAExpiry,
    int? I9ListAFileAttachmentId,
    bool HasListADocNumber,
    string? I9ListBType,
    string? I9ListBAuthority,
    DateTimeOffset? I9ListBExpiry,
    int? I9ListBFileAttachmentId,
    bool HasListBDocNumber,
    string? I9ListCType,
    string? I9ListCAuthority,
    DateTimeOffset? I9ListCExpiry,
    int? I9ListCFileAttachmentId,
    bool HasListCDocNumber,

    // Step 6
    string? BankName,
    string? AccountType,
    bool HasBankRouting,
    bool HasBankAccount
);
