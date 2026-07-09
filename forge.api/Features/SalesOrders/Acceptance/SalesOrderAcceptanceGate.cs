using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// The customer-acceptance production gate. When CAP-O2C-SO-ACCEPTANCE is enabled, a Sales Order
/// cannot be confirmed and no Job may be linked to its lines until an <see cref="AcceptanceStatus.Accepted"/>
/// acceptance record exists. Channel-agnostic — it only asks "is there an Accepted row for this SO",
/// so every capture channel (upload / portal / e-signature / …) satisfies it identically. When the
/// capability is off, the gate is transparent (never blocks).
/// </summary>
public interface ISalesOrderAcceptanceGate
{
    /// <summary>True when the capability toggle is on for this install.</summary>
    bool IsEnabled { get; }

    /// <summary>An Accepted acceptance record exists for the sales order.</summary>
    Task<bool> IsAcceptedAsync(int salesOrderId, CancellationToken ct = default);

    /// <summary>Throws (→ 409) when the gate is enabled and the sales order has no accepted proof.</summary>
    Task EnsureReleasableAsync(int salesOrderId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class SalesOrderAcceptanceGate(AppDbContext db, ICapabilitySnapshotProvider capabilities)
    : ISalesOrderAcceptanceGate
{
    private const string Capability = "CAP-O2C-SO-ACCEPTANCE";

    public bool IsEnabled => capabilities.IsEnabled(Capability);

    public Task<bool> IsAcceptedAsync(int salesOrderId, CancellationToken ct = default)
        => db.SalesOrderAcceptances.AsNoTracking()
            .AnyAsync(a => a.SalesOrderId == salesOrderId && a.Status == AcceptanceStatus.Accepted, ct);

    public async Task EnsureReleasableAsync(int salesOrderId, CancellationToken ct = default)
    {
        if (!IsEnabled) return;
        if (!await IsAcceptedAsync(salesOrderId, ct))
            throw new InvalidOperationException(
                "Customer acceptance proof is required before this sales order can be released to production. " +
                "Attach acceptance (signed document, portal, or e-signature) on the order first.");
    }
}
