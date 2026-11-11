using System.Globalization;
using System.Security.Claims;

using FluentValidation;
using FluentValidation.Results;

using Forge.Api.Authorization;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Tolerances;

/// <summary>
/// Raising an over-quantity tolerance widens what a receipt or shipment accepts without an override, so it
/// takes one of <see cref="ToleranceOverrideRoles"/>. Other callers may lower it or leave it. The comparison
/// is on the percentage in effect, so clearing a value that sits below the level it falls back to is a raise.
/// Under-quantity tolerances are not guarded.
/// </summary>
public static class ToleranceRaiseGuard
{
    public const string Code = "tolerance-raise-not-allowed";

    public static async Task EnsureVendorAsync(
        AppDbContext? db, decimal? before, decimal? after, string property, ClaimsPrincipal? user, CancellationToken ct)
    {
        if (after == before || ToleranceOverrideRoles.CanOverride(user))
            return;
        var fallback = db is null ? 0m : (await QuantityTolerances.PurchaseDefaultsAsync(db, ct)).OverPercent;
        Ensure(property, before ?? fallback, after ?? fallback);
    }

    public static async Task EnsureCustomerAsync(
        AppDbContext? db, decimal? before, decimal? after, string property, ClaimsPrincipal? user, CancellationToken ct)
    {
        if (after == before || ToleranceOverrideRoles.CanOverride(user))
            return;
        var fallback = db is null ? 0m : (await QuantityTolerances.SalesDefaultsAsync(db, ct)).OverPercent;
        Ensure(property, before ?? fallback, after ?? fallback);
    }

    /// <summary>
    /// Checks order lines against their vendor's level. A null <c>After</c> is a line that sends no value and
    /// keeps what it has.
    /// </summary>
    public static async Task EnsurePurchaseLinesAsync(
        AppDbContext? db, int vendorId, IReadOnlyCollection<(string Property, decimal? Before, decimal? After)> lines,
        ClaimsPrincipal? user, CancellationToken ct)
    {
        if (!lines.Any(Changes) || ToleranceOverrideRoles.CanOverride(user))
            return;
        var party = (await QuantityTolerances.ForVendorAsync(db, vendorId, ct))(new PurchaseOrderLine()).OverPercent;
        foreach (var line in lines.Where(Changes))
            Ensure(line.Property, line.Before ?? party, line.After!.Value);
    }

    /// <summary>
    /// Checks order lines against their customer's level. A null <c>After</c> is a line that sends no value and
    /// keeps what it has.
    /// </summary>
    public static async Task EnsureSalesLinesAsync(
        AppDbContext? db, int customerId, IReadOnlyCollection<(string Property, decimal? Before, decimal? After)> lines,
        ClaimsPrincipal? user, CancellationToken ct)
    {
        if (!lines.Any(Changes) || ToleranceOverrideRoles.CanOverride(user))
            return;
        var party = (await QuantityTolerances.ForCustomerAsync(db, customerId, ct))(new SalesOrderLine()).OverPercent;
        foreach (var line in lines.Where(Changes))
            Ensure(line.Property, line.Before ?? party, line.After!.Value);
    }

    private static bool Changes((string Property, decimal? Before, decimal? After) line)
        => line.After is not null && line.After != line.Before;

    private static void Ensure(string property, decimal before, decimal after)
    {
        if (after <= before)
            return;
        throw new ValidationException([
            new ValidationFailure(property, string.Format(CultureInfo.InvariantCulture,
                "Only an Admin or Manager can raise an over tolerance. It is {0:0.##}% now; lower it or leave it.", before))
            {
                ErrorCode = Code,
                FormattedMessagePlaceholderValues = new Dictionary<string, object?> { ["ComparisonValue"] = before },
            },
        ]);
    }
}
