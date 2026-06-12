using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Banking;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ BANKING BOUNDARY — BANK-002 Phase A: vendor bank accounts (encrypted at rest, masked on
/// every response, dual-control approvals, prenote lifecycle) and NACHA payment batches
/// (assemble → generate → download → manual portal upload → release-by-second-user = SoD).
/// Gated by <c>CAP-BANK-NACHA</c> (default OFF — enable once the bank's ACH origination
/// agreement is in place and the Banking settings are populated).
/// </summary>
[ApiController]
[Route("api/v1/banking")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-BANK-NACHA")]
public class BankingController(
    IVendorBankAccountService bankAccounts,
    IPaymentBatchService batches,
    IBankReturnsService returns) : ControllerBase
{
    // ── Vendor bank accounts ──────────────────────────────────────────────

    [HttpGet("bank-accounts")]
    public async Task<ActionResult<IReadOnlyList<VendorBankAccountModel>>> GetBankAccounts(
        [FromQuery] int? vendorId, [FromQuery] string? status, CancellationToken ct)
        => Ok(await bankAccounts.ListAsync(vendorId, status, ct));

    [HttpPost("vendors/{vendorId:int}/bank-accounts")]
    public async Task<ActionResult<VendorBankAccountModel>> CreateBankAccount(
        int vendorId, SaveVendorBankAccountRequestModel request, CancellationToken ct)
        => Ok(await bankAccounts.CreateAsync(vendorId, request, UserId, ct));

    [HttpPut("bank-accounts/{id:int}")]
    public async Task<ActionResult<VendorBankAccountModel>> UpdateBankAccount(
        int id, SaveVendorBankAccountRequestModel request, CancellationToken ct)
        => Ok(await bankAccounts.UpdateNumbersAsync(id, request, UserId, ct));

    [HttpPost("bank-accounts/{id:int}/approve")]
    public async Task<ActionResult<VendorBankAccountModel>> ApproveBankAccount(int id, CancellationToken ct)
        => Ok(await bankAccounts.ApproveAsync(id, UserId, ct));

    [HttpPost("bank-accounts/{id:int}/mark-verified")]
    public async Task<ActionResult<VendorBankAccountModel>> MarkBankAccountVerified(int id, CancellationToken ct)
        => Ok(await bankAccounts.MarkVerifiedAsync(id, UserId, ct));

    [HttpPost("bank-accounts/{id:int}/disable")]
    public async Task<ActionResult<VendorBankAccountModel>> DisableBankAccount(int id, CancellationToken ct)
        => Ok(await bankAccounts.DisableAsync(id, UserId, ct));

    // ── Payment batches ───────────────────────────────────────────────────

    [HttpGet("payment-batches")]
    public async Task<ActionResult<IReadOnlyList<PaymentBatchListItemModel>>> GetBatches(CancellationToken ct)
        => Ok(await batches.ListAsync(ct));

    [HttpGet("payment-batches/{id:int}")]
    public async Task<ActionResult<PaymentBatchDetailModel>> GetBatch(int id, CancellationToken ct)
        => Ok(await batches.GetDetailAsync(id, ct));

    [HttpGet("payment-batches/eligible-payments")]
    public async Task<ActionResult<IReadOnlyList<BatchEligiblePaymentModel>>> GetEligiblePayments(CancellationToken ct)
        => Ok(await batches.GetEligiblePaymentsAsync(ct));

    [HttpPost("payment-batches")]
    public async Task<ActionResult<PaymentBatchDetailModel>> CreateBatch(
        CreatePaymentBatchRequestModel request, CancellationToken ct)
        => Ok(await batches.CreateAsync(
            request.VendorPaymentIds, DateOnly.FromDateTime(request.EffectiveEntryDate.UtcDateTime), UserId, ct));

    [HttpPost("payment-batches/prenote")]
    public async Task<ActionResult<PaymentBatchDetailModel>> CreatePrenoteBatch(
        CreatePrenoteBatchRequestModel request, CancellationToken ct)
        => Ok(await batches.CreatePrenoteBatchAsync(
            DateOnly.FromDateTime(request.EffectiveEntryDate.UtcDateTime), UserId, ct));

    [HttpPost("payment-batches/{id:int}/generate")]
    public async Task<ActionResult<PaymentBatchDetailModel>> GenerateBatch(int id, CancellationToken ct)
        => Ok(await batches.GenerateAsync(id, UserId, ct));

    /// <summary>The generated NACHA file, exactly as stored — for manual upload to the bank portal.</summary>
    [HttpGet("payment-batches/{id:int}/file")]
    public async Task<IActionResult> DownloadBatchFile(int id, CancellationToken ct)
    {
        var (fileName, contents) = await batches.GetFileAsync(id, ct);
        return File(System.Text.Encoding.ASCII.GetBytes(contents), "text/plain", fileName);
    }

    [HttpPost("payment-batches/{id:int}/release")]
    public async Task<ActionResult<PaymentBatchDetailModel>> ReleaseBatch(int id, CancellationToken ct)
        => Ok(await batches.ReleaseAsync(id, UserId, ct));

    [HttpPost("payment-batches/{id:int}/cancel")]
    public async Task<ActionResult<PaymentBatchDetailModel>> CancelBatch(int id, CancellationToken ct)
        => Ok(await batches.CancelAsync(id, UserId, ct));

    // ── Returns / NOC ingestion (Phase C — NACHA-standard, bank-agnostic) ──

    /// <summary>Upload a bank ACH return/NOC file. Idempotent — re-imports double-apply nothing.</summary>
    [HttpPost("returns/import")]
    public async Task<ActionResult<BankReturnsImportResultModel>> ImportReturns(IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var contents = await reader.ReadToEndAsync(ct);
        return Ok(await returns.ApplyAsync(contents, UserId, ct));
    }

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
