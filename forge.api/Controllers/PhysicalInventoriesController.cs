using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory.PhysicalInventories;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// The full physical inventory (CAP-INV-PHYSICAL): create, freeze, count sheets, count entry by tag (desktop and
/// phone), variance review, post and cancel. Counting is open to the stock roles the scanner serves; creating,
/// freezing, reviewing, posting and cancelling are Admin and Manager. The ledger side of a post is written as the
/// system (the count is the business action).
/// </summary>
[ApiController]
[Route("api/v1/inventory/physical-inventory")]
[Authorize(Roles = CountRoles)]
[RequiresCapability("CAP-INV-PHYSICAL")]
public class PhysicalInventoriesController(IMediator mediator) : ControllerBase
{
    private const string CountRoles = "Admin,Manager,OfficeManager,Engineer,ProductionWorker";
    private const string ManageRoles = "Admin,Manager";

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>The physical inventories, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PhysicalInventoryResponseModel>>> List(
        [FromQuery] PhysicalInventoryStatus? status, CancellationToken ct)
        => Ok(await mediator.Send(new GetPhysicalInventoriesQuery(status), ct));

    /// <summary>One physical inventory with its tags; a blind count hides book quantities until review.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PhysicalInventoryDetailResponseModel>> Get(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetPhysicalInventoryQuery(id), ct));

    /// <summary>One tag by its number (a scanned or typed tag).</summary>
    [HttpGet("{id:int}/tags/by-number/{tagNumber:int}")]
    public async Task<ActionResult<PhysicalInventoryTagResponseModel>> GetTag(int id, int tagNumber, CancellationToken ct)
        => Ok(await mediator.Send(new GetPhysicalInventoryTagQuery(id, tagNumber), ct));

    /// <summary>The count sheets PDF, one row per tag plus blank rows for found stock.</summary>
    [HttpGet("{id:int}/count-sheets")]
    public async Task<IActionResult> CountSheets(int id, [FromQuery] bool? blind, [FromQuery] string? language, CancellationToken ct)
    {
        var (content, fileName) = await mediator.Send(new GetPhysicalInventoryCountSheetsQuery(id, blind, language), ct);
        return File(content, "application/pdf", fileName);
    }

    /// <summary>Creates a draft over a location and everything under it, or the whole site.</summary>
    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    public async Task<ActionResult<PhysicalInventoryDetailResponseModel>> Create(
        [FromBody] CreatePhysicalInventoryRequestModel request, CancellationToken ct)
    {
        var created = await mediator.Send(new CreatePhysicalInventoryCommand(request, UserId), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Snapshots the book quantities onto tags and blocks stock movements in scope.</summary>
    [HttpPost("{id:int}/freeze")]
    [Authorize(Roles = ManageRoles)]
    public async Task<ActionResult<PhysicalInventoryDetailResponseModel>> Freeze(int id, CancellationToken ct)
        => Ok(await mediator.Send(new FreezePhysicalInventoryCommand(id, UserId), ct));

    /// <summary>Adds a tag for stock found in scope without one.</summary>
    [HttpPost("{id:int}/tags")]
    public async Task<ActionResult<PhysicalInventoryTagResponseModel>> AddTag(
        int id, [FromBody] AddPhysicalInventoryTagRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new AddPhysicalInventoryTagCommand(id, request, UserId), ct));

    /// <summary>Records a tag's count, or its recount when one was asked for.</summary>
    [HttpPut("{id:int}/tags/{tagId:int}/count")]
    public async Task<ActionResult<PhysicalInventoryTagResponseModel>> EnterCount(
        int id, int tagId, [FromBody] EnterPhysicalCountRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new EnterPhysicalCountCommand(id, tagId, request, UserId), ct));

    /// <summary>Closes counting and opens the variance review.</summary>
    [HttpPost("{id:int}/review")]
    [Authorize(Roles = ManageRoles)]
    public async Task<ActionResult<PhysicalInventoryDetailResponseModel>> SubmitForReview(int id, CancellationToken ct)
        => Ok(await mediator.Send(new SubmitPhysicalInventoryForReviewCommand(id), ct));

    /// <summary>Sends a reviewed inventory back to counting.</summary>
    [HttpPost("{id:int}/reopen")]
    [Authorize(Roles = ManageRoles)]
    public async Task<ActionResult<PhysicalInventoryDetailResponseModel>> Reopen(int id, CancellationToken ct)
        => Ok(await mediator.Send(new ReopenPhysicalInventoryCountingCommand(id), ct));

    /// <summary>Book against counted quantity per tag, valued at current cost.</summary>
    [HttpGet("{id:int}/variance")]
    [Authorize(Roles = ManageRoles)]
    public async Task<ActionResult<PhysicalInventoryVarianceResponseModel>> Variance(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetPhysicalInventoryVarianceQuery(id), ct));

    /// <summary>Posts the reviewed counts to stock, valuation and (with CAP-ACCT-FULLGL) the ledger.</summary>
    [HttpPost("{id:int}/post")]
    [Authorize(Roles = ManageRoles)]
    public async Task<ActionResult<PhysicalInventoryDetailResponseModel>> Post(int id, CancellationToken ct)
        => Ok(await mediator.Send(new PostPhysicalInventoryCommand(id, UserId), ct));

    /// <summary>Cancels the inventory and releases the freeze without changing stock.</summary>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = ManageRoles)]
    public async Task<ActionResult<PhysicalInventoryDetailResponseModel>> Cancel(int id, CancellationToken ct)
        => Ok(await mediator.Send(new CancelPhysicalInventoryCommand(id, UserId), ct));
}
