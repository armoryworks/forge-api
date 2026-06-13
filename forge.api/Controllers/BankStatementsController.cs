using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ BANK-001 — bank statement import + auto-match staging. Import an OFX/CSV statement against
/// a cash GL account; auto-match proposes one-to-one candidates; confirming a match clears the
/// journal line in the open bank reconciliation (the actual-settlement attestation for
/// in-transit cash). Same gate as the reconciliation screens.
/// </summary>
[ApiController]
[Route("api/v1/accounting/bank-statements")]
[Authorize(Roles = "Controller")]
[RequiresCapability("CAP-ACCT-FULLGL")]
public class BankStatementsController(IBankStatementImportService service) : ControllerBase
{
    /// <summary>Upload one statement file (multipart). Dedupes on FITID; auto-match runs inline.</summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportBankStatementResultModel>> Import(
        [FromForm] int bookId, [FromForm] int cashGlAccountId, IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var contents = await reader.ReadToEndAsync(ct);
        return Ok(await service.ImportAsync(bookId, cashGlAccountId, file.FileName, contents, UserId, ct));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BankStatementImportModel>>> GetImports(
        [FromQuery] int? cashGlAccountId, CancellationToken ct)
        => Ok(await service.ListImportsAsync(cashGlAccountId, ct));

    [HttpGet("{importId:int}/lines")]
    public async Task<ActionResult<IReadOnlyList<BankStatementLineModel>>> GetLines(
        int importId, [FromQuery] string? status, CancellationToken ct)
        => Ok(await service.GetLinesAsync(importId, status, ct));

    /// <summary>Re-runs auto-match over the import's unmatched lines; returns the new suggestion count.</summary>
    [HttpPost("{importId:int}/auto-match")]
    public async Task<ActionResult<int>> AutoMatch(int importId, CancellationToken ct)
        => Ok(await service.AutoMatchAsync(importId, ct));

    [HttpPost("lines/{lineId:long}/confirm")]
    public async Task<ActionResult<BankStatementLineModel>> Confirm(long lineId, CancellationToken ct)
        => Ok(await service.ConfirmAsync(lineId, UserId, ct));

    [HttpPost("lines/{lineId:long}/match/{journalLineId:long}")]
    public async Task<ActionResult<BankStatementLineModel>> ManualMatch(
        long lineId, long journalLineId, CancellationToken ct)
        => Ok(await service.ManualMatchAsync(lineId, journalLineId, UserId, ct));

    [HttpPost("lines/{lineId:long}/ignore")]
    public async Task<ActionResult<BankStatementLineModel>> Ignore(long lineId, CancellationToken ct)
        => Ok(await service.IgnoreAsync(lineId, ct));

    [HttpPost("lines/{lineId:long}/unmatch")]
    public async Task<ActionResult<BankStatementLineModel>> Unmatch(long lineId, CancellationToken ct)
        => Ok(await service.UnmatchAsync(lineId, ct));

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
