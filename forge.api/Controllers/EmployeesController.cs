using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Activity;
using Forge.Api.Features.Employees;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/employees")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-MD-EMPLOYEES")]
public class EmployeesController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 3 F7-broad / WU-22 — standardised paged-list contract.
    ///
    /// New shape:
    ///   <c>GET /employees?page=1&amp;pageSize=25&amp;sort=lastName&amp;order=asc&amp;q=jane&amp;isActive=true&amp;teamId=4&amp;role=Manager&amp;department=Engineering&amp;dateFrom=2025-01-01</c>
    ///
    /// Response: <c>{ items, totalCount, page, pageSize }</c>.
    ///
    /// Backward compat: the legacy <c>?search=&amp;teamId=&amp;role=&amp;isActive=</c>
    /// form continues to work — when both <c>q</c> and <c>search</c> are
    /// present, <c>q</c> wins. Existing UI callers that don't pass any query
    /// params get the standard default (page 1, 25 records,
    /// lastName/firstName asc).
    ///
    /// Manager restriction is enforced server-side: non-admin callers only
    /// see employees in their own team.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<EmployeeListItemResponseModel>>> GetEmployees(
        [FromQuery] EmployeeListQuery query,
        [FromQuery(Name = "search")] string? legacySearch,
        CancellationToken ct)
    {
        var callerUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var callerIsAdmin = User.IsInRole("Admin");

        var effective = string.IsNullOrEmpty(query.Q) && !string.IsNullOrEmpty(legacySearch)
            ? query with { Q = legacySearch }
            : query;

        var result = await mediator.Send(new GetEmployeeListQuery(effective, callerUserId, callerIsAdmin), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeDetailResponseModel>> GetEmployee(int id)
    {
        var result = await mediator.Send(new GetEmployeeDetailQuery(id));
        if (result == null) return NotFound();
        return Ok(result);
    }

    // ── Phase 3 / WU-19 / F9 — Employee/User split (1:0-1) ──
    //
    // Backend pass: Employee can exist with no User account. The existing
    // UI onboarding flow (creates User + Employee in one shot) continues to
    // work — its path is unchanged. New endpoints below cover the User-less
    // case so HR can onboard an employee before IT provisions a system
    // account.

    /// <summary>
    /// Create an Employee record without requiring a User account
    /// (Phase 3 / WU-19 / F9). The returned <c>id</c> is the
    /// EmployeeProfile.Id, which is then accepted by
    /// <c>GET /api/v1/employees/{id}</c>, the grant- and
    /// revoke-system-access endpoints, and <c>DELETE /api/v1/employees/{id}</c>.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<EmployeeProfileResponseModel>> CreateEmployee(
        [FromBody] CreateEmployeeCommand request, CancellationToken ct)
    {
        var result = await mediator.Send(request, ct);
        return Created($"/api/v1/employees/{result.Id}", result);
    }

    /// <summary>
    /// Promote an existing Employee to also have a User account
    /// (grant system access). Mirrors the <c>POST /api/v1/admin/users</c>
    /// path on the User side — issues a setup token and links the User to
    /// the EmployeeProfile.
    /// </summary>
    [HttpPost("{employeeId:int}/grant-system-access")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<GrantSystemAccessResponseModel>> GrantSystemAccess(
        int employeeId,
        [FromBody] GrantSystemAccessRequest request,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new GrantSystemAccessCommand(employeeId, request.Email, request.Role), ct);
        return Ok(result);
    }

    /// <summary>
    /// Un-link the User from an Employee while preserving both records.
    /// The User is deactivated (preserves audit history); the Employee
    /// remains and can be re-promoted later.
    /// </summary>
    [HttpDelete("{employeeId:int}/system-access")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RevokeSystemAccess(int employeeId, CancellationToken ct)
    {
        var ok = await mediator.Send(new RevokeSystemAccessCommand(employeeId), ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Soft-delete an Employee (by EmployeeProfile.Id). If a User is linked
    /// the User is left in place (deactivated) so audit history is preserved.
    /// </summary>
    [HttpDelete("{employeeId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteEmployee(int employeeId, CancellationToken ct)
    {
        var ok = await mediator.Send(new DeleteEmployeeCommand(employeeId), ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpGet("{id:int}/stats")]
    public async Task<ActionResult<EmployeeStatsResponseModel>> GetEmployeeStats(int id)
    {
        var result = await mediator.Send(new GetEmployeeStatsQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/time-summary")]
    public async Task<ActionResult<List<EmployeeTimeEntryItem>>> GetTimeSummary(int id, [FromQuery] string? period)
    {
        var result = await mediator.Send(new GetEmployeeTimeSummaryQuery(id, period));
        return Ok(result);
    }

    [HttpGet("{id:int}/pay-summary")]
    public async Task<ActionResult<List<EmployeePayStubItem>>> GetPaySummary(int id)
    {
        var result = await mediator.Send(new GetEmployeePaySummaryQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/jobs")]
    public async Task<ActionResult<List<EmployeeJobItem>>> GetJobs(int id)
    {
        var result = await mediator.Send(new GetEmployeeJobsQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/expenses")]
    public async Task<ActionResult<List<EmployeeExpenseItem>>> GetExpenses(int id)
    {
        var result = await mediator.Send(new GetEmployeeExpensesQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/training")]
    public async Task<ActionResult<List<EmployeeTrainingItem>>> GetTraining(int id)
    {
        var result = await mediator.Send(new GetEmployeeTrainingQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/compliance")]
    public async Task<ActionResult<List<EmployeeComplianceItem>>> GetCompliance(int id)
    {
        var result = await mediator.Send(new GetEmployeeComplianceQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<List<ActivityEntryResponseModel>>> GetActivity(int id)
    {
        var result = await mediator.Send(new GetEntityActivityQuery("Employee", id));
        return Ok(result);
    }
}
