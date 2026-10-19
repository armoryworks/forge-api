using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorPayments;

/// <summary>
/// Decides whether a transmission is a wire waiting for manual attestation. New waiting wires carry
/// <see cref="PaymentTransmissionStatus.AwaitingAttestation"/>. Wires created before that status existed
/// were left Queued with no attempt; they are recognised by the <c>awaiting-wire-attestation</c> activity
/// row their payment logged, by never having been attempted and by never having been re-queued by hand.
/// </summary>
public static class WireAttestationState
{
    public const string AwaitingActivityAction = "awaiting-wire-attestation";
    public const string RetriedActivityAction = "transmission-retried";

    /// <summary>True when no channel job may submit <paramref name="transmission"/>: it waits for a second user's attestation.</summary>
    public static async Task<bool> IsAwaitingAttestationAsync(
        AppDbContext db, PaymentTransmission transmission, CancellationToken ct)
    {
        if (transmission.Status == PaymentTransmissionStatus.AwaitingAttestation)
            return true;

        if (transmission.Status != PaymentTransmissionStatus.Queued
            || transmission.LastAttemptAt is not null
            || transmission.AttemptCount != 0
            || transmission.SourceType != "VendorPayment"
            || !string.Equals(transmission.Method, nameof(PaymentMethod.Wire), StringComparison.OrdinalIgnoreCase))
            return false;

        var actions = await db.ActivityLogs.AsNoTracking()
            .Where(a => a.EntityType == "VendorPayment"
                && a.EntityId == transmission.SourceId
                && (a.Action == AwaitingActivityAction || a.Action == RetriedActivityAction))
            .Select(a => a.Action)
            .ToListAsync(ct);
        return actions.Contains(AwaitingActivityAction) && !actions.Contains(RetriedActivityAction);
    }
}
