using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Integration seam for originating electronic payments (ACH / wire) through the bank channel.
/// Mock-only today: the real adapter (NACHA file vs bank API — Frontier CU channel is an open
/// decision, see docs/accounting/architecture.md §10) plugs in behind this same interface.
/// </summary>
public interface IBankPaymentService
{
    /// <summary>Submits one payment to the bank. Returns a failure result (or throws) on channel errors.</summary>
    Task<BankSubmissionResult> SubmitPaymentAsync(BankPaymentRequest request, CancellationToken ct);
}
