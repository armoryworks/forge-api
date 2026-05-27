using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Activity;
using Forge.Api.Features.Expenses;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/expenses")]
[Authorize]
[RequiresCapability("CAP-ACCT-EXPENSES")]
public class ExpensesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ExpenseResponseModel>>> GetExpenses(
        [FromQuery] int? userId,
        [FromQuery] ExpenseStatus? status,
        [FromQuery] string? search)
    {
        var result = await mediator.Send(new GetExpensesQuery(userId, status, search));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseResponseModel>> CreateExpense([FromBody] CreateExpenseRequestModel request)
    {
        var result = await mediator.Send(new CreateExpenseCommand(request));
        return Created($"/api/v1/expenses/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ExpenseResponseModel>> UpdateExpense(int id, [FromBody] UpdateExpenseRequestModel request)
    {
        var result = await mediator.Send(new UpdateExpenseCommand(id, request));
        return Ok(result);
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")] // F-EXP-01: approval is an approver-role action
    public async Task<ActionResult<ExpenseResponseModel>> UpdateExpenseStatus(int id, [FromBody] UpdateExpenseStatusRequestModel request)
    {
        var result = await mediator.Send(new UpdateExpenseStatusCommand(id, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteExpense(int id)
    {
        await mediator.Send(new DeleteExpenseCommand(id));
        return NoContent();
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetExpenseActivity(int id)
    {
        var result = await mediator.Send(new GetEntityActivityQuery("Expense", id));
        return Ok(result);
    }

    [HttpPost("receipts")]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult<FileAttachmentResponseModel>> UploadReceipt(IFormFile file)
    {
        var result = await mediator.Send(new UploadExpenseReceiptCommand(file));
        return Created($"/api/v1/files/{result.Id}", result);
    }

    [HttpGet("settings")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ExpenseSettingsResponse>> GetSettings()
    {
        var result = await mediator.Send(new GetExpenseSettingsQuery());
        return Ok(result);
    }

    [HttpPut("settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateExpenseSettingsCommand command)
    {
        await mediator.Send(command);
        return NoContent();
    }

    // ─── Recurring Expenses ───

    [HttpGet("recurring")]
    public async Task<ActionResult<List<RecurringExpenseResponseModel>>> GetRecurringExpenses(
        [FromQuery] string? classification)
    {
        var result = await mediator.Send(new GetRecurringExpensesQuery(classification));
        return Ok(result);
    }

    [HttpPost("recurring")]
    public async Task<ActionResult<RecurringExpenseResponseModel>> CreateRecurringExpense(
        [FromBody] CreateRecurringExpenseRequestModel request)
    {
        var result = await mediator.Send(new CreateRecurringExpenseCommand(request));
        return Created($"/api/v1/expenses/recurring/{result.Id}", result);
    }

    [HttpPatch("recurring/{id:int}")]
    public async Task<ActionResult<RecurringExpenseResponseModel>> UpdateRecurringExpense(
        int id, [FromBody] UpdateRecurringExpenseRequestModel request)
    {
        var result = await mediator.Send(new UpdateRecurringExpenseCommand(id, request));
        return Ok(result);
    }

    [HttpDelete("recurring/{id:int}")]
    public async Task<IActionResult> DeleteRecurringExpense(int id)
    {
        await mediator.Send(new DeleteRecurringExpenseCommand(id));
        return NoContent();
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<List<UpcomingExpenseResponseModel>>> GetUpcomingExpenses(
        [FromQuery] int daysAhead = 90,
        [FromQuery] string? classification = null)
    {
        var result = await mediator.Send(new GetUpcomingExpensesQuery(daysAhead, classification));
        return Ok(result);
    }
}
