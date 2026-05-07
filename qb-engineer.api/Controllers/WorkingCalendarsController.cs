using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QBEngineer.Api.Capabilities;
using QBEngineer.Api.Features.WorkingCalendars;
using QBEngineer.Core.Models;

namespace QBEngineer.Api.Controllers;

/// <summary>
/// Working calendars + holidays. Drives every business-day helper in the
/// system (vendor lead time, MRP, maintenance schedules, SLA timers).
/// Tenant-default + per-CompanyLocation override; resolution at runtime
/// is handled by IWorkingCalendarService.
///
/// <para>Gated behind CAP-MD-CALENDARS (already in the catalog as the
/// "Calendars (shifts, holidays, downtime)" capability — re-using it
/// rather than minting a new code).</para>
/// </summary>
[ApiController]
[Route("api/v1/working-calendars")]
[Authorize(Roles = "Admin")]
[RequiresCapability("CAP-MD-CALENDARS")]
public class WorkingCalendarsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WorkingCalendarResponseModel>>> GetAll()
    {
        var result = await mediator.Send(new GetWorkingCalendarsQuery());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkingCalendarResponseModel>> Get(int id)
    {
        var result = await mediator.Send(new GetWorkingCalendarQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<WorkingCalendarResponseModel>> Create(WorkingCalendarRequestModel request)
    {
        var result = await mediator.Send(new CreateWorkingCalendarCommand(
            request.Name, request.TimeZone, request.WorkingDaysMask, request.IsActive));
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkingCalendarResponseModel>> Update(int id, WorkingCalendarRequestModel request)
    {
        var result = await mediator.Send(new UpdateWorkingCalendarCommand(
            id, request.Name, request.TimeZone, request.WorkingDaysMask, request.IsActive));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteWorkingCalendarCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/set-default")]
    public async Task<IActionResult> SetDefault(int id)
    {
        await mediator.Send(new SetDefaultWorkingCalendarCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/holidays")]
    public async Task<ActionResult<HolidayResponseModel>> AddHoliday(int id, HolidayRequestModel request)
    {
        var result = await mediator.Send(new AddHolidayCommand(
            id, request.Date, request.Name, request.ObservedDate, request.IsRecurring));
        return Created(string.Empty, result);
    }

    [HttpDelete("{id:int}/holidays/{holidayId:int}")]
    public async Task<IActionResult> DeleteHoliday(int id, int holidayId)
    {
        await mediator.Send(new DeleteHolidayCommand(id, holidayId));
        return NoContent();
    }

    // Shifts effort — calendar-bound shift CRUD. Nested under the calendar
    // since shifts are owned by their calendar (cascade delete; no
    // standalone existence). Read path is via Get(id) on this controller —
    // the response embeds the shifts list + computed weekly capacity.

    [HttpPost("{id:int}/shifts")]
    public async Task<ActionResult<CalendarShiftResponseModel>> AddShift(
        int id, CalendarShiftRequestModel request)
    {
        var result = await mediator.Send(new AddCalendarShiftCommand(id, request));
        return Created(string.Empty, result);
    }

    [HttpPut("{id:int}/shifts/{shiftId:int}")]
    public async Task<ActionResult<CalendarShiftResponseModel>> UpdateShift(
        int id, int shiftId, CalendarShiftRequestModel request)
    {
        var result = await mediator.Send(new UpdateCalendarShiftCommand(id, shiftId, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}/shifts/{shiftId:int}")]
    public async Task<IActionResult> DeleteShift(int id, int shiftId)
    {
        await mediator.Send(new DeleteCalendarShiftCommand(id, shiftId));
        return NoContent();
    }
}
