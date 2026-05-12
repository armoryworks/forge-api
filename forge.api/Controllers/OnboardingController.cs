using System.Security.Claims;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Onboarding;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/onboarding")]
[Authorize]
[RequiresCapability("CAP-HR-HIRE")]
public class OnboardingController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetEmail() => User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    private string GetName() => User.FindFirstValue(ClaimTypes.Name) ?? GetEmail();

    /// <summary>
    /// Submits the unified onboarding wizard. Fills government PDFs, submits to DocuSeal,
    /// persists profile data, and marks acknowledgments complete.
    /// </summary>
    [HttpPost("submit")]
    public async Task<ActionResult<OnboardingSubmitResultModel>> Submit(
        [FromBody] OnboardingSubmitRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(
            new SubmitOnboardingCommand(GetUserId(), GetEmail(), GetName(), model), ct);
        return Ok(result);
    }

    /// <summary>
    /// Pre-uploads an I-9 identity document (List A, B, or C) before the wizard is submitted.
    /// Returns a FileAttachmentId to be included in the main submission payload.
    /// </summary>
    [HttpPost("i9-document")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadI9DocumentResultModel>> UploadI9Document(
        IFormFile file,
        [FromForm] string documentList,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UploadI9DocumentCommand(GetUserId(), file, documentList), ct);
        return Ok(result);
    }

    /// <summary>
    /// Stores a voided check image uploaded during the direct deposit step.
    /// Returns a FileAttachmentId to be included in the main submission payload.
    /// </summary>
    [HttpPost("voided-check")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadI9DocumentResultModel>> UploadVoidedCheck(
        IFormFile file,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UploadVoidedCheckCommand(GetUserId(), file), ct);
        return Ok(result);
    }

    /// <summary>Returns completion status for all onboarding items for the current user.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<OnboardingStatusModel>> GetStatus(CancellationToken ct)
    {
        var result = await mediator.Send(new GetOnboardingStatusQuery(GetUserId()), ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns the policy-document URLs (workers' comp notice, employee handbook)
    /// surfaced on the Acknowledgments step. Blank values map to null so the UI
    /// can omit the link and — for the handbook — skip the acknowledgment entirely.
    /// </summary>
    [HttpGet("policy-docs")]
    public async Task<ActionResult<OnboardingPolicyDocsModel>> GetPolicyDocs(CancellationToken ct)
    {
        var result = await mediator.Send(new GetOnboardingPolicyDocsQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Server-side onboarding draft — partial save. Each step's Continue posts
    /// the fields the user filled in. Sensitive identifiers (SSN, bank
    /// routing/account, I-9 doc numbers) are encrypted via IPiiProtector
    /// before being written; passing null leaves the existing ciphertext
    /// alone (so a re-visit doesn't wipe previously stored values). Replaces
    /// the localStorage draft we previously used — see fix(6c5eae1) for the
    /// security rationale.
    /// </summary>
    [HttpPost("draft")]
    public async Task<ActionResult<OnboardingDraftStatusModel>> SaveDraft(
        [FromBody] SaveOnboardingDraftRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new SaveOnboardingDraftCommand(GetUserId(), model), ct);
        return Ok(result);
    }

    /// <summary>
    /// Current onboarding-draft state for the calling user. Non-sensitive
    /// values come back verbatim; sensitive identifiers are represented only
    /// by Has* booleans (no plaintext / no ciphertext) so the wizard can
    /// render a "Securely stored — re-enter to overwrite" indicator.
    /// </summary>
    [HttpGet("draft")]
    public async Task<ActionResult<OnboardingDraftStatusModel>> GetDraft(CancellationToken ct)
    {
        var result = await mediator.Send(new GetOnboardingDraftStatusQuery(GetUserId()), ct);
        return Ok(result);
    }

    /// <summary>
    /// Self-service bypass: marks the current user as onboarded without completing the wizard.
    /// Creates an EmployeeProfile if one doesn't exist and sets OnboardingBypassedAt.
    /// </summary>
    [HttpPost("bypass")]
    public async Task<IActionResult> Bypass(CancellationToken ct)
    {
        await mediator.Send(new BypassOnboardingCommand(GetUserId()), ct);
        return NoContent();
    }

    // ── Per-form review flow ──────────────────────────────────────────────────

    /// <summary>
    /// Persists profile data, identity documents, and acknowledgments without touching DocuSeal.
    /// Returns the ordered list of compliance forms that the employee needs to review and sign.
    /// Call this once after the employee completes all wizard steps, then use preview-pdf /
    /// sign-form in a loop for each returned form.
    /// </summary>
    [HttpPost("save")]
    public async Task<ActionResult<SaveOnboardingResultModel>> Save(
        [FromBody] OnboardingSubmitRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(
            new SaveOnboardingDataCommand(GetUserId(), GetEmail(), GetName(), model), ct);
        return Ok(result);
    }

    /// <summary>
    /// Fills a single compliance form PDF with the employee's data and returns it
    /// as a base64-encoded string for in-browser preview.
    /// No DocuSeal interaction, no database writes.
    /// Returns HasTemplate=false when the template has not been configured for PDF pre-fill.
    /// </summary>
    [HttpPost("preview-pdf")]
    public async Task<ActionResult<PreviewOnboardingPdfResultModel>> PreviewPdf(
        [FromBody] PreviewOnboardingPdfRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new PreviewOnboardingPdfCommand(GetUserId(), model), ct);
        return Ok(result);
    }

    /// <summary>
    /// Fills a single compliance form PDF and creates a DocuSeal signing submission for it.
    /// Returns the employee-facing signing URL and submission ID.
    /// </summary>
    [HttpPost("sign-form")]
    public async Task<ActionResult<SignOnboardingFormResultModel>> SignForm(
        [FromBody] SignOnboardingFormRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(
            new SignOnboardingFormCommand(GetUserId(), GetEmail(), GetName(), model), ct);
        return Ok(result);
    }
}
