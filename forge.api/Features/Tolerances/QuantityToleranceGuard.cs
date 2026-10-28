using System.Globalization;
using System.Security.Claims;

using Forge.Api.Authorization;
using Forge.Core.Models;

namespace Forge.Api.Features.Tolerances;

/// <summary>
/// The over-tolerance check shared by purchase receipts, subcontract receive-backs and shipments. Inside the
/// band nothing is asked. Above it the caller must confirm, hold one of <see cref="ToleranceOverrideRoles"/>
/// and give a reason; otherwise <see cref="QuantityToleranceException"/> refuses it.
/// </summary>
public static class QuantityToleranceGuard
{
    public const int ReasonMaxLength = 500;

    /// <summary>
    /// Returns null when <paramref name="total"/> is inside the band, or the trimmed override reason when it is
    /// above the band and the override is accepted.
    /// </summary>
    public static string? EnsureAccepted(
        string kind,
        int lineId,
        decimal ordered,
        decimal alreadyDone,
        decimal requested,
        QuantityTolerance tolerance,
        bool confirm,
        string? reason,
        ClaimsPrincipal? user,
        string verb,
        string subject)
    {
        var total = alreadyDone + requested;
        if (!tolerance.Exceeds(ordered, total))
            return null;

        var canOverride = ToleranceOverrideRoles.CanOverride(user);
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (confirm && canOverride && trimmed is not null)
            return trimmed.Length <= ReasonMaxLength ? trimmed : trimmed[..ReasonMaxLength];

        var max = tolerance.MaxAccepted(ordered);
        var room = Math.Max(0m, max - alreadyDone);
        var message = string.Format(CultureInfo.InvariantCulture,
            "Cannot {0} {1:0.####} of {2} — only {3:0.####} remaining{4}.",
            verb, requested, subject, room,
            tolerance.OverPercent > 0m
                ? string.Format(CultureInfo.InvariantCulture, " within the {0:0.##}% over tolerance", tolerance.OverPercent)
                : string.Empty);
        message += canOverride
            ? " Confirm it and give a reason to accept the extra."
            : " A manager can accept the extra with a reason.";

        throw new QuantityToleranceException(message, new QuantityToleranceViolationResponseModel(
            kind, lineId, ordered, alreadyDone, requested, max, tolerance.OverPercent, tolerance.UnderPercent, canOverride));
    }
}
