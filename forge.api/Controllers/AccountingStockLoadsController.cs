using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting.StockLoads;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Controllers;

/// <summary>
/// §7A opening inventory: a costed stock load (quantity and cutover unit cost per part, location and lot) that
/// posts the bins, the valuation store and Dr inventory / Cr Opening Balance Equity, so stock and the ledger agree
/// from the start. Controller only; open under either <c>CAP-ACCT-MIGRATION</c> (cutover, FULLGL still off) or
/// <c>CAP-ACCT-FULLGL</c>. Not offered in connected-accounting mode (the dry run refuses it).
/// </summary>
[ApiController]
[Route("api/v1/accounting/conversion/stock-loads")]
[Authorize(Roles = "Controller")]
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public class AccountingStockLoadsController(IMediator mediator) : ControllerBase
{
    /// <summary>The book's stock loads, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StockLoadSummaryModel>>> List([FromQuery] int? bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetStockLoadsQuery(bookId), ct));

    /// <summary>The book, the posted opening journal's date and the inventory debits it already carries.</summary>
    [HttpGet("context")]
    public async Task<ActionResult<StockLoadContextModel>> GetContext([FromQuery] int? bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetStockLoadContextQuery(bookId), ct));

    /// <summary>Inventory GL vs valuation-store tie-out for the book.</summary>
    [HttpGet("reconciliation")]
    public async Task<ActionResult<InventoryValuationReconciliation>> GetReconciliation([FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetStockLoadReconciliationQuery(bookId), ct));

    /// <summary>One stock load with its lines.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<StockLoadModel>> Get(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetStockLoadQuery(id), ct));

    /// <summary>Creates a draft load from JSON lines or the CSV template's text.</summary>
    [HttpPost]
    public async Task<ActionResult<StockLoadModel>> Create([FromBody] CreateStockLoadCommand command, CancellationToken ct)
    {
        var created = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Replaces a draft load's header and lines.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<StockLoadModel>> Update(int id, [FromBody] UpdateStockLoadCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command with { Id = id }, ct));

    /// <summary>Deletes a draft load.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteStockLoadCommand(id), ct);
        return NoContent();
    }

    /// <summary>The dry run: per-line errors and warnings and totals per inventory account. No writes.</summary>
    [HttpPost("{id:int}/validate")]
    public async Task<ActionResult<StockLoadValidationResult>> Validate(int id, CancellationToken ct)
        => Ok(await mediator.Send(new ValidateStockLoadQuery(id), ct));

    /// <summary>Re-validates and posts a draft load.</summary>
    [HttpPost("{id:int}/post")]
    public async Task<ActionResult<StockLoadModel>> Post(int id, CancellationToken ct)
        => Ok(await mediator.Send(new PostStockLoadCommand(id), ct));

    /// <summary>Voids a posted load while its stock is still where it was put.</summary>
    [HttpPost("{id:int}/void")]
    public async Task<ActionResult<StockLoadModel>> Void(int id, CancellationToken ct)
        => Ok(await mediator.Send(new VoidStockLoadCommand(id), ct));
}
