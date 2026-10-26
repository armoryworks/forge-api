using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Controllers;

/// <summary>
/// §7A opening receivables and payables: the customer invoices and vendor bills still open at cutover, checked
/// (party, amount, dates, document number) and posted as the book's opening-balance entry, so AR/AP aging and
/// customer statements carry them from the start. Controller only; open under either <c>CAP-ACCT-MIGRATION</c>
/// (cutover, FULLGL still off) or <c>CAP-ACCT-FULLGL</c>, like the opening stock loads.
/// </summary>
[ApiController]
[Route("api/v1/accounting/conversion/open-items")]
[Authorize(Roles = "Controller")]
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public class AccountingOpenItemsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Dry run of the open items: each row's resolved party and problems, the receivable and payable totals for
    /// the tie-out, and whether the book's opening-balance entry is already posted. Saves nothing.
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<OpeningOpenItemsValidationModel>> Validate(
        [FromBody] ValidateOpeningOpenItemsRequestModel model, CancellationToken ct)
        => Ok(await mediator.Send(new ValidateOpeningOpenItemsQuery(model), ct));

    /// <summary>Posts the open items once they check clean and tie to the legacy AR and AP control balances.</summary>
    [HttpPost]
    public async Task<ActionResult<OpeningBalanceResult>> Post(
        [FromBody] PostOpeningOpenItemsRequestModel model, CancellationToken ct)
        => Ok(await mediator.Send(new PostOpeningOpenItemsCommand(model), ct));
}
