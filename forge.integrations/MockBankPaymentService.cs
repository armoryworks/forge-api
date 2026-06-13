using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Mock bank-payment channel. Succeeds with a deterministic <c>MOCK-ACH-*</c> reference UNLESS the
/// request's reference number contains "FAIL" (case-insensitive) — that forced failure lets the
/// retry/backoff/triage path be exercised end-to-end without a real bank connection.
/// </summary>
public class MockBankPaymentService(ILogger<MockBankPaymentService> logger) : IBankPaymentService
{
    public Task<BankSubmissionResult> SubmitPaymentAsync(BankPaymentRequest request, CancellationToken ct)
    {
        if (request.ReferenceNumber?.Contains("FAIL", StringComparison.OrdinalIgnoreCase) == true)
        {
            logger.LogWarning(
                "[MockBank] Forced failure for {SourceType} {SourceId} (reference contains FAIL): {Amount:C} via {Method}",
                request.SourceType, request.SourceId, request.Amount, request.Method);
            return Task.FromResult(new BankSubmissionResult(
                false, null, "Mock bank API unavailable (forced by reference FAIL)"));
        }

        var submissionRef = $"MOCK-ACH-{request.SourceType}-{request.SourceId}";
        logger.LogInformation(
            "[MockBank] Submitted {SourceType} {SourceId} to {Vendor}: {Amount:C} via {Method} → {Ref}",
            request.SourceType, request.SourceId, request.VendorName ?? "(unknown vendor)",
            request.Amount, request.Method, submissionRef);

        return Task.FromResult(new BankSubmissionResult(true, submissionRef, null));
    }
}
