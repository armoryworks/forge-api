using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounts;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/accounts")]
[Authorize(Roles = "Admin,Manager,PM")]
[RequiresCapability("CAP-O2C-LEAD")]
public class AccountsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AccountResponseModel>>> Get()
        => Ok(await mediator.Send(new GetAccountsQuery()));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccountResponseModel>> GetById(int id)
        => Ok(await mediator.Send(new GetAccountByIdQuery(id)));

    [HttpPost]
    public async Task<ActionResult<AccountResponseModel>> Create([FromBody] CreateAccountRequest request)
    {
        var result = await mediator.Send(new CreateAccountCommand(request));
        return Created($"/api/v1/accounts/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AccountResponseModel>> Update(int id, [FromBody] UpdateAccountRequest request)
        => Ok(await mediator.Send(new UpdateAccountCommand(id, request)));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteAccountCommand(id));
        return NoContent();
    }

    // ── Contacts ──────────────────────────────────────────────────

    [HttpGet("{id:int}/contacts")]
    public async Task<ActionResult<List<AccountContactResponseModel>>> GetContacts(int id)
        => Ok(await mediator.Send(new GetAccountContactsQuery(id)));

    [HttpPost("{id:int}/contacts")]
    public async Task<ActionResult<AccountContactResponseModel>> CreateContact(int id, [FromBody] UpsertAccountContactRequest request)
    {
        var result = await mediator.Send(new CreateAccountContactCommand(id, request));
        return Created($"/api/v1/accounts/{id}/contacts/{result.Id}", result);
    }

    [HttpPut("{id:int}/contacts/{contactId:int}")]
    public async Task<ActionResult<AccountContactResponseModel>> UpdateContact(int id, int contactId, [FromBody] UpsertAccountContactRequest request)
        => Ok(await mediator.Send(new UpdateAccountContactCommand(id, contactId, request)));

    [HttpDelete("{id:int}/contacts/{contactId:int}")]
    public async Task<IActionResult> DeleteContact(int id, int contactId)
    {
        await mediator.Send(new DeleteAccountContactCommand(id, contactId));
        return NoContent();
    }
}
