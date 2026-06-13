using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Edi;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/edi")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-CROSS-INTEG-EDI")]
public class EdiController(IMediator mediator, IEdiPartNumberMapService partNumberMap) : ControllerBase
{
    // ── Trading Partners ────────────────────────────────────────

    [HttpGet("trading-partners")]
    public async Task<ActionResult<List<EdiTradingPartnerResponseModel>>> GetTradingPartners(
        [FromQuery] bool? isActive)
    {
        var result = await mediator.Send(new GetEdiTradingPartnersQuery(isActive));
        return Ok(result);
    }

    [HttpGet("trading-partners/{id:int}")]
    public async Task<ActionResult<EdiTradingPartnerResponseModel>> GetTradingPartner(int id)
    {
        var result = await mediator.Send(new GetEdiTradingPartnerByIdQuery(id));
        return Ok(result);
    }

    [HttpPost("trading-partners")]
    public async Task<ActionResult<EdiTradingPartnerResponseModel>> CreateTradingPartner(
        [FromBody] CreateEdiTradingPartnerRequestModel model)
    {
        var result = await mediator.Send(new CreateEdiTradingPartnerCommand(model));
        return CreatedAtAction(nameof(GetTradingPartner), new { id = result.Id }, result);
    }

    [HttpPut("trading-partners/{id:int}")]
    public async Task<ActionResult<EdiTradingPartnerResponseModel>> UpdateTradingPartner(
        int id, [FromBody] UpdateEdiTradingPartnerRequestModel model)
    {
        var result = await mediator.Send(new UpdateEdiTradingPartnerCommand(id, model));
        return Ok(result);
    }

    [HttpDelete("trading-partners/{id:int}")]
    public async Task<IActionResult> DeleteTradingPartner(int id)
    {
        await mediator.Send(new DeleteEdiTradingPartnerCommand(id));
        return NoContent();
    }

    [HttpPost("trading-partners/{id:int}/test")]
    public async Task<ActionResult<TestEdiConnectionResult>> TestConnection(int id)
    {
        var result = await mediator.Send(new TestEdiConnectionCommand(id));
        return Ok(result);
    }

    // ── Transactions ────────────────────────────────────────────

    [HttpGet("transactions")]
    public async Task<ActionResult<PaginatedResponse<EdiTransactionResponseModel>>> GetTransactions(
        [FromQuery] EdiDirection? direction,
        [FromQuery] string? transactionSet,
        [FromQuery] EdiTransactionStatus? status,
        [FromQuery] int? tradingPartnerId,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await mediator.Send(new GetEdiTransactionsQuery(
            direction, transactionSet, status, tradingPartnerId, dateFrom, dateTo, page, pageSize));
        return Ok(result);
    }

    [HttpGet("transactions/{id:int}")]
    public async Task<ActionResult<EdiTransactionDetailResponseModel>> GetTransaction(int id)
    {
        var result = await mediator.Send(new GetEdiTransactionByIdQuery(id));
        return Ok(result);
    }

    [HttpPost("receive")]
    public async Task<ActionResult<EdiTransactionResponseModel>> ReceiveDocument(
        [FromBody] ReceiveEdiDocumentRequestModel model)
    {
        var result = await mediator.Send(new ReceiveEdiDocumentCommand(model));
        return CreatedAtAction(nameof(GetTransaction), new { id = result.Id }, result);
    }

    [HttpPost("send/{entityType}/{entityId:int}")]
    public async Task<ActionResult<EdiTransactionResponseModel>> SendOutbound(
        string entityType, int entityId, [FromBody] SendOutboundEdiRequestModel model)
    {
        var result = await mediator.Send(new SendOutboundEdiCommand(entityType, entityId, model));
        return CreatedAtAction(nameof(GetTransaction), new { id = result.Id }, result);
    }

    [HttpPost("transactions/{id:int}/retry")]
    public async Task<IActionResult> RetryTransaction(int id)
    {
        await mediator.Send(new RetryEdiTransactionCommand(id));
        return NoContent();
    }

    // ── Mappings ────────────────────────────────────────────────

    [HttpGet("trading-partners/{id:int}/mappings")]
    public async Task<ActionResult<List<EdiMappingResponseModel>>> GetMappings(int id)
    {
        var result = await mediator.Send(new GetEdiMappingsQuery(id));
        return Ok(result);
    }

    [HttpPost("trading-partners/{id:int}/mappings")]
    public async Task<ActionResult<EdiMappingResponseModel>> CreateMapping(
        int id, [FromBody] CreateEdiMappingRequestModel model)
    {
        var result = await mediator.Send(new CreateEdiMappingCommand(id, model));
        return Created($"/api/v1/edi/mappings/{result.Id}", result);
    }

    [HttpPut("mappings/{id:int}")]
    public async Task<ActionResult<EdiMappingResponseModel>> UpdateMapping(
        int id, [FromBody] UpdateEdiMappingRequestModel model)
    {
        var result = await mediator.Send(new UpdateEdiMappingCommand(id, model));
        return Ok(result);
    }

    [HttpDelete("mappings/{id:int}")]
    public async Task<IActionResult> DeleteMapping(int id)
    {
        await mediator.Send(new DeleteEdiMappingCommand(id));
        return NoContent();
    }

    // ── Part-number translation (per partner; typed rows + CSV import) ──────

    /// <summary>The partner's part-number map (partner number → our part), each row resolved against the catalog.</summary>
    [HttpGet("trading-partners/{id:int}/part-number-map")]
    public async Task<ActionResult<IReadOnlyList<EdiPartNumberMapRow>>> GetPartNumberMap(int id, CancellationToken ct)
        => Ok(await partNumberMap.GetRowsAsync(id, ct));

    /// <summary>Replaces the partner's entire part-number map with the supplied typed rows.</summary>
    [HttpPut("trading-partners/{id:int}/part-number-map")]
    public async Task<ActionResult<IReadOnlyList<EdiPartNumberMapRow>>> SavePartNumberMap(
        int id, [FromBody] SaveEdiPartNumberMapRequestModel request, CancellationToken ct)
        => Ok(await partNumberMap.ReplaceRowsAsync(id, request.Rows, ct));

    /// <summary>Bulk-imports a two-column CSV (PartnerPartNumber, OurPartNumber); upserts by partner number.</summary>
    [HttpPost("trading-partners/{id:int}/part-number-map/import")]
    public async Task<ActionResult<EdiPartNumberMapImportResultModel>> ImportPartNumberMap(
        int id, IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var csv = await reader.ReadToEndAsync(ct);
        return Ok(await partNumberMap.ImportCsvAsync(id, csv, ct));
    }
}
