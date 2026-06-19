using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Forge.Core.Entities;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// Computes a shipment's Forge-issued, coverage-bound scan token — the value encoded in the master
/// QR on the printed label wrapper and validated by the scan-to-ship gate. Provider-agnostic: it
/// identifies the shipment by Forge's own number and binds the exact coverage, so a stale reprint
/// (taken before the lines changed) no longer validates. Deterministic: the same shipment number and
/// the same coverage always produce the same code.
/// </summary>
public static class ShipmentScanCode
{
    public const string Version = "v1";

    /// <summary>
    /// The master scan code for the whole shipment — the value the scan-to-ship gate validates and the
    /// master QR encodes. <c>v1.{shipmentNumber}.{coverageHash}</c> over every line's coverage.
    /// </summary>
    public static string Compute(string shipmentNumber, IEnumerable<ShipmentLine> lines)
        => $"{Version}.{shipmentNumber}.{CoverageHash(lines)}";

    /// <summary>
    /// A scoped scan code for one slice of the shipment (e.g. a single sales order within a multi-order
    /// shipment): <c>v1.{shipmentNumber}.{scope}.{coverageHash}</c>. The <paramref name="scope"/> segment
    /// (e.g. <c>S42</c>) keeps it distinct from the master even when the scope covers the whole shipment,
    /// and binds it to just the lines passed in. Rendered as the per-SO QR; not gate-validated yet.
    /// </summary>
    public static string ComputeForScope(string shipmentNumber, string scope, IEnumerable<ShipmentLine> lines)
        => $"{Version}.{shipmentNumber}.{scope}.{CoverageHash(lines)}";

    private static string CoverageHash(IEnumerable<ShipmentLine> lines)
    {
        // Canonicalize coverage: one "key:qty" token per line, sorted, joined. The key is the SO line
        // (falling back to the part) so the hash pins which partial-or-full SO content this set covers —
        // the "intersection of data" that distinguishes two shipments of the same quantities.
        var coverage = lines
            .Select(l => string.Create(CultureInfo.InvariantCulture,
                $"{(l.SalesOrderLineId is int sol ? $"L{sol}" : $"P{l.PartId}")}:{l.Quantity:0.####}"))
            .OrderBy(s => s, StringComparer.Ordinal);

        var canonical = string.Join("|", coverage);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        // 9 bytes → 12 base64url chars: ample collision resistance for a per-shipment gate, short
        // enough to render cleanly in a QR and stay readable in a log line.
        return Convert.ToBase64String(hash, 0, 9)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
