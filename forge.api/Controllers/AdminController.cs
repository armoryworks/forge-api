using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Admin;
using Forge.Api.Features.CompanyLocations;
using Forge.Api.Features.EmployeeProfile;
using Forge.Api.Features.IntegrationOutbox;
using Forge.Api.Features.ReferenceData;
using Forge.Api.Features.ShiftAssignments;
using Forge.Api.Features.TrackTypes;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(IMediator mediator) : ControllerBase
{
    // ── Roles ──

    [HttpGet("roles")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<RoleItem>>> GetRoles()
    {
        var result = await mediator.Send(new GetRolesQuery());
        return Ok(result);
    }

    // ── Users ──

    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserResponseModel>>> GetUsers()
    {
        var result = await mediator.Send(new GetAdminUsersQuery());
        return Ok(result);
    }

    [HttpPost("users")]
    public async Task<ActionResult<CreateAdminUserResponseModel>> CreateUser(CreateAdminUserCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(GetUsers), result);
    }

    [HttpPut("users/{id:int}")]
    public async Task<ActionResult<AdminUserResponseModel>> UpdateUser(int id, UpdateAdminUserCommand command)
    {
        var cmd = command with { Id = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    // ── Role Bundles (role templates) ──
    // Named bundles of underlying roles, seeded at install
    // (FrontOffice / FloorLead / OwnerOperator). No longer assigned to users
    // (user assignment is direct multi-role); a bundle is what a SystemApiKey
    // scopes its emitted role claims to. CRUD only — no user-assignment surface.

    [HttpGet("role-templates")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<RoleTemplateResponseModel>>> GetRoleTemplates(
        [FromQuery] bool includeDeactivated = false)
    {
        var result = await mediator.Send(new GetRoleTemplatesQuery(includeDeactivated));
        return Ok(result);
    }

    [HttpPost("role-templates")]
    public async Task<ActionResult<RoleTemplateResponseModel>> CreateRoleTemplate(CreateRoleTemplateCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(GetRoleTemplates), new { id = result.Id }, result);
    }

    [HttpPut("role-templates/{id:int}")]
    public async Task<ActionResult<RoleTemplateResponseModel>> UpdateRoleTemplate(int id, UpdateRoleTemplateCommand command)
    {
        var cmd = command with { Id = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpDelete("role-templates/{id:int}")]
    public async Task<IActionResult> DeleteRoleTemplate(int id)
    {
        await mediator.Send(new DeleteRoleTemplateCommand(id));
        return NoContent();
    }


    // ── Track Types ──

    [HttpGet("track-types")]
    public async Task<ActionResult<List<TrackTypeResponseModel>>> GetTrackTypes()
    {
        var result = await mediator.Send(new GetTrackTypesQuery());
        return Ok(result);
    }

    [HttpPost("track-types")]
    public async Task<ActionResult<TrackTypeResponseModel>> CreateTrackType(CreateTrackTypeCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(GetTrackTypes), result);
    }

    [HttpPut("track-types/{id:int}")]
    public async Task<ActionResult<TrackTypeResponseModel>> UpdateTrackType(int id, UpdateTrackTypeCommand command)
    {
        var cmd = command with { Id = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpDelete("track-types/{id:int}")]
    public async Task<ActionResult> DeleteTrackType(int id)
    {
        await mediator.Send(new DeleteTrackTypeCommand(id));
        return NoContent();
    }

    // ── Reference Data ──

    [HttpGet("reference-data")]
    public async Task<ActionResult<List<ReferenceDataGroupResponseModel>>> GetReferenceData()
    {
        var result = await mediator.Send(new GetReferenceDataGroupsQuery());
        return Ok(result);
    }

    [HttpPost("reference-data")]
    public async Task<ActionResult<ReferenceDataResponseModel>> CreateReferenceData(CreateReferenceDataCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(GetReferenceData), result);
    }

    [HttpPut("reference-data/{id:int}")]
    public async Task<ActionResult<ReferenceDataResponseModel>> UpdateReferenceData(int id, UpdateReferenceDataCommand command)
    {
        var cmd = command with { Id = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpDelete("reference-data/{id:int}")]
    public async Task<IActionResult> DeleteReferenceData(int id)
    {
        await mediator.Send(new DeleteReferenceDataCommand(id));
        return NoContent();
    }

    // ── Brand Settings (public — no auth required for login screen theming) ──

    [AllowAnonymous]
    [HttpGet("brand")]
    public async Task<ActionResult<BrandSettingsResponseModel>> GetBrandSettings()
    {
        var result = await mediator.Send(new GetBrandSettingsQuery());
        return Ok(result);
    }

    // ── Logo ──

    [AllowAnonymous]
    [HttpGet("logo")]
    public async Task<IActionResult> GetLogo()
    {
        var result = await mediator.Send(new GetLogoQuery());
        if (result == null) return NotFound();
        return File(result.Stream, result.ContentType);
    }

    [HttpPost("logo")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        if (file.Length == 0) return BadRequest("No file provided");
        if (!file.ContentType.StartsWith("image/")) return BadRequest("File must be an image");

        await using var stream = file.OpenReadStream();
        await mediator.Send(new UploadLogoCommand(stream, file.ContentType));
        return NoContent();
    }

    [HttpDelete("logo")]
    public async Task<IActionResult> DeleteLogo()
    {
        await mediator.Send(new DeleteLogoCommand());
        return NoContent();
    }

    // ── Branding lockups (public read — login screen needs them pre-auth) ──
    // GET serves the admin-uploaded file if present, else the bundled Forge default
    // from wwwroot/branding-defaults/. The {kind} segment is "wordmark" or "marquee".
    // The optional ?theme=light query swaps the default to the light variant.

    [AllowAnonymous]
    [HttpGet("branding/{kind}")]
    public async Task<IActionResult> GetLockup(string kind, [FromQuery] string? theme = null)
    {
        if (!Enum.TryParse<LockupKind>(kind, ignoreCase: true, out var parsedKind))
            return BadRequest("kind must be 'wordmark', 'marquee', or 'favicon'");

        var parsedTheme = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)
            ? LockupTheme.Light
            : LockupTheme.Dark;

        var result = await mediator.Send(new GetLockupQuery(parsedKind, parsedTheme));
        if (result == null) return NotFound();
        return File(result.Stream, result.ContentType);
    }

    [HttpPost("branding/{kind}")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadLockup(string kind, IFormFile file)
    {
        if (!Enum.TryParse<LockupKind>(kind, ignoreCase: true, out var parsedKind))
            return BadRequest("kind must be 'wordmark', 'marquee', or 'favicon'");

        if (file.Length == 0) return BadRequest("No file provided");
        if (!file.ContentType.StartsWith("image/")) return BadRequest("File must be an image");

        await using var stream = file.OpenReadStream();
        await mediator.Send(new UploadLockupCommand(parsedKind, stream, file.ContentType));
        return NoContent();
    }

    [HttpDelete("branding/{kind}")]
    public async Task<IActionResult> DeleteLockup(string kind)
    {
        if (!Enum.TryParse<LockupKind>(kind, ignoreCase: true, out var parsedKind))
            return BadRequest("kind must be 'wordmark', 'marquee', or 'favicon'");

        await mediator.Send(new DeleteLockupCommand(parsedKind));
        return NoContent();
    }

    // ── System Settings ──

    [HttpGet("system-settings")]
    public async Task<ActionResult<List<SystemSettingResponseModel>>> GetSystemSettings()
    {
        var result = await mediator.Send(new GetSystemSettingsQuery());
        return Ok(result);
    }

    [HttpPut("system-settings")]
    public async Task<ActionResult<List<SystemSettingResponseModel>>> UpsertSystemSettings(UpsertSystemSettingsCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    // ── Setup Token & Invite ──

    [HttpPost("users/{id:int}/setup-token")]
    public async Task<ActionResult<SetupTokenResponseModel>> GenerateSetupToken(int id)
    {
        var result = await mediator.Send(new GenerateSetupTokenCommand(id));
        return Ok(result);
    }

    [HttpPost("users/{id:int}/send-invite")]
    public async Task<IActionResult> SendSetupInvite(int id, [FromQuery] string baseUrl)
    {
        await mediator.Send(new SendSetupInviteCommand(id, baseUrl));
        return NoContent();
    }

    [HttpPost("users/{id:int}/reset-pin")]
    public async Task<IActionResult> ResetUserPin(int id)
    {
        await mediator.Send(new ResetUserPinCommand(id));
        return NoContent();
    }

    // ── User Lifecycle ──

    [HttpPost("users/{id:int}/deactivate")]
    public async Task<IActionResult> DeactivateUser(int id)
    {
        await mediator.Send(new DeactivateUserCommand(id));
        return NoContent();
    }

    [HttpPost("users/{id:int}/reactivate")]
    public async Task<IActionResult> ReactivateUser(int id)
    {
        await mediator.Send(new ReactivateUserCommand(id));
        return NoContent();
    }

    // ── Employee Documents / Certifications ──

    [HttpGet("users/{id:int}/documents")]
    public async Task<ActionResult<List<EmployeeDocumentResponseModel>>> GetEmployeeDocuments(int id)
    {
        var result = await mediator.Send(new GetEmployeeDocumentsQuery(id));
        return Ok(result);
    }

    // ── Audit Log ──

    [HttpGet("audit-log")]
    [RequiresCapability("CAP-IDEN-AUDIT-SYSTEM-LOG")]
    public async Task<ActionResult<PaginatedResult<AuditLogEntryResponseModel>>> GetAuditLog(
        [FromQuery] int? userId, [FromQuery] string? action, [FromQuery] string? entityType,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var result = await mediator.Send(new GetAuditLogQuery(userId, action, entityType, from, to, page, pageSize));
        return Ok(result);
    }

    // ── Scan Identifiers (NFC/RFID/Barcode) ──

    [HttpGet("users/{userId:int}/scan-identifiers")]
    public async Task<ActionResult<List<ScanIdentifierResponseModel>>> GetScanIdentifiers(int userId)
    {
        var result = await mediator.Send(new GetUserScanIdentifiersQuery(userId));
        return Ok(result);
    }

    [HttpPost("users/{userId:int}/scan-identifiers")]
    public async Task<IActionResult> AddScanIdentifier(int userId, [FromBody] AddScanIdentifierRequestModel request)
    {
        var result = await mediator.Send(new AddScanIdentifierCommand(userId, request.IdentifierType, request.IdentifierValue));
        return Created($"/api/v1/admin/users/{userId}/scan-identifiers/{result.Id}", result);
    }

    [HttpDelete("users/{userId:int}/scan-identifiers/{id:int}")]
    public async Task<IActionResult> RemoveScanIdentifier(int userId, int id)
    {
        await mediator.Send(new RemoveScanIdentifierCommand(id));
        return NoContent();
    }

    // ── Storage Usage ──

    [HttpGet("storage-usage")]
    public async Task<ActionResult<List<StorageUsageResponseModel>>> GetStorageUsage()
    {
        var result = await mediator.Send(new GetStorageUsageQuery());
        return Ok(result);
    }

    // ── Employee Profiles ──

    [HttpGet("users/{userId:int}/employee-profile")]
    public async Task<ActionResult<EmployeeProfileResponseModel>> GetEmployeeProfile(int userId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetAdminEmployeeProfileQuery(userId), ct);
        return Ok(result);
    }

    [HttpPut("users/{userId:int}/employee-profile")]
    public async Task<ActionResult<EmployeeProfileResponseModel>> UpdateEmployeeProfile(
        int userId, [FromBody] AdminUpdateEmployeeProfileRequestModel data, CancellationToken ct)
    {
        var result = await mediator.Send(new AdminUpdateEmployeeProfileCommand(userId, data), ct);
        return Ok(result);
    }

    // ── Work Location Assignment ──

    [HttpPatch("users/{userId:int}/work-location")]
    public async Task<IActionResult> UpdateUserWorkLocation(int userId, [FromBody] UpdateUserWorkLocationRequestModel request)
    {
        await mediator.Send(new UpdateUserWorkLocationCommand(userId, request.WorkLocationId));
        return NoContent();
    }

    // ── Integrations ──

    [HttpGet("integrations")]
    public async Task<ActionResult<IntegrationSettingsResult>> GetIntegrations()
    {
        var result = await mediator.Send(new GetIntegrationSettingsQuery());
        return Ok(result);
    }

    [HttpPut("integrations/{provider}")]
    public async Task<ActionResult<IntegrationStatusModel>> UpdateIntegration(string provider, [FromBody] UpdateIntegrationSettingsRequestModel request)
    {
        var result = await mediator.Send(new UpdateIntegrationSettingsCommand(provider, request.Settings));
        return Ok(result);
    }

    [HttpPost("integrations/{provider}/test")]
    public async Task<ActionResult<TestIntegrationResultModel>> TestIntegration(string provider)
    {
        var result = await mediator.Send(new TestIntegrationConnectionCommand(provider));
        return Ok(result);
    }

    // ── Company Profile ──

    [HttpGet("company-profile")]
    public async Task<ActionResult<CompanyProfileResponseModel>> GetCompanyProfile()
    {
        var result = await mediator.Send(new GetCompanyProfileQuery());
        return Ok(result);
    }

    [HttpPatch("company-profile")]
    public async Task<ActionResult<CompanyProfileResponseModel>> UpdateCompanyProfile(CompanyProfileRequestModel request)
    {
        var result = await mediator.Send(new UpdateCompanyProfileCommand(
            request.Name, request.Phone, request.Email, request.Ein, request.Website));
        return Ok(result);
    }

    // ── Labor Rates ──

    [HttpGet("labor-rates/{userId:int}")]
    public async Task<ActionResult<List<LaborRateResponseModel>>> GetLaborRates(int userId, CancellationToken ct)
        => Ok(await mediator.Send(new GetLaborRatesQuery(userId), ct));

    [HttpPost("labor-rates")]
    public async Task<ActionResult<LaborRateResponseModel>> CreateLaborRate(
        [FromBody] CreateLaborRateRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateLaborRateCommand(
            request.UserId, request.StandardRatePerHour, request.OvertimeRatePerHour,
            request.DoubletimeRatePerHour, request.EffectiveFrom, request.Notes), ct);
        return Created($"/api/v1/admin/labor-rates/{result.UserId}", result);
    }

    // ── Shift Assignments ──

    [HttpGet("shift-assignments")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<ShiftAssignmentResponseModel>>> GetShiftAssignments(
        [FromQuery] int? userId, CancellationToken ct)
        => Ok(await mediator.Send(new GetShiftAssignmentsQuery(userId), ct));

    [HttpPost("shift-assignments")]
    public async Task<ActionResult<ShiftAssignmentResponseModel>> CreateShiftAssignment(
        [FromBody] CreateShiftAssignmentRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateShiftAssignmentCommand(request), ct);
        return Created($"/api/v1/admin/shift-assignments/{result.Id}", result);
    }

    [HttpDelete("shift-assignments/{id:int}")]
    public async Task<IActionResult> DeleteShiftAssignment(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteShiftAssignmentCommand(id), ct);
        return NoContent();
    }

    // ── MFA Policy ──────────────────────────────────────

    [HttpGet("mfa/compliance")]
    [RequiresCapability("CAP-IDEN-AUTH-MFA")]
    public async Task<IActionResult> GetMfaCompliance(CancellationToken ct)
    {
        var result = await mediator.Send(new GetMfaPolicyStatusQuery(), ct);
        return Ok(result);
    }

    [HttpPut("mfa/policy")]
    [RequiresCapability("CAP-IDEN-AUTH-MFA")]
    public async Task<IActionResult> SetMfaPolicy([FromBody] MfaPolicyRequestModel request, CancellationToken ct)
    {
        await mediator.Send(new SetMfaPolicyCommand(request.RequiredRoles), ct);
        return NoContent();
    }

    // ── Integration Outbox ──

    [HttpGet("integration-outbox")]
    public async Task<ActionResult<List<OutboxEntryResponseModel>>> GetOutboxEntries(
        [FromQuery] OutboxStatus? status,
        [FromQuery] IntegrationProvider? provider,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetOutboxEntriesQuery(status, provider, take), ct);
        return Ok(result);
    }

    [HttpPost("integration-outbox/{id:int}/retry")]
    public async Task<IActionResult> RetryOutboxEntry(int id, CancellationToken ct)
    {
        await mediator.Send(new RetryOutboxEntryCommand(id), ct);
        return NoContent();
    }

    [HttpPost("integration-outbox/{id:int}/discard")]
    public async Task<IActionResult> DiscardOutboxEntry(int id, CancellationToken ct)
    {
        await mediator.Send(new DiscardOutboxEntryCommand(id), ct);
        return NoContent();
    }
}

public record CreateLaborRateRequest(
    int UserId,
    decimal StandardRatePerHour,
    decimal OvertimeRatePerHour,
    decimal? DoubletimeRatePerHour,
    DateOnly EffectiveFrom,
    string? Notes);
