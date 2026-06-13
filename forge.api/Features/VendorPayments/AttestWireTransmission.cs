using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorPayments;

/// <summary>
/// banking.wire.manual-attestation — the SoD step for wires: someone keys the wire into the bank
/// portal by hand, then a DIFFERENT user attests it here. Attestation flips the Queued
/// transmission to Succeeded (submission accepted — settlement is still confirmed by BANK-001
/// statement matching) and posts the cash-in-transit settlement entry exactly as the automated
/// channel does. The mirror of payment-batch release.
/// </summary>
public record AttestWireTransmissionCommand(int VendorPaymentId, string? BankReference) : IRequest;

public class AttestWireTransmissionHandler(
    AppDbContext db,
    ITransmissionSettlementService settlement,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<AttestWireTransmissionCommand>
{
    public async Task Handle(AttestWireTransmissionCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(
            httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var payment = await db.VendorPayments
            .FirstOrDefaultAsync(p => p.Id == request.VendorPaymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor payment {request.VendorPaymentId} not found");

        var transmission = await db.PaymentTransmissions
            .Where(t => t.SourceType == "VendorPayment" && t.SourceId == payment.Id)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The payment has no bank transmission to attest.");

        if (payment.Method != PaymentMethod.Wire)
            throw new InvalidOperationException("Only wire payments are attested manually.");
        if (transmission.Status != PaymentTransmissionStatus.Queued)
            throw new InvalidOperationException(
                $"The transmission is {transmission.Status}; only a Queued wire awaits attestation.");

        // SoD: the wire's creator keys it at the portal; a DIFFERENT user attests (mirror of
        // batch release — one person can never both move and confirm money).
        if (transmission.CreatedByUserId == userId)
            throw new InvalidOperationException(
                "Segregation of duties: a wire must be attested by a different user than the one who created the payment.");

        transmission.Status = PaymentTransmissionStatus.Succeeded;
        transmission.AttemptCount = 1;
        transmission.LastAttemptAt = DateTimeOffset.UtcNow;
        transmission.SubmissionRef = string.IsNullOrWhiteSpace(request.BankReference)
            ? $"MANUAL-WIRE/{payment.PaymentNumber}"
            : $"MANUAL-WIRE/{request.BankReference.Trim()}";

        db.LogActivityAt(
            "wire-attested",
            $"Wire {payment.PaymentNumber} ({payment.Amount:C}) attested as entered at the bank (ref {transmission.SubmissionRef})",
            ("VendorPayment", payment.Id));
        await db.SaveChangesAsync(cancellationToken);

        // Same mechanical consequence as the automated channel's success: clear cash-in-transit.
        await settlement.TryPostSettlementAsync(transmission, payment.PaymentNumber, cancellationToken);
    }
}
