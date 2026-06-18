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

    public static string Compute(string shipmentNumber, IEnumerable<ShipmentLine> lines)
    {
        // Canonicalize coverage: one "key:qty" token per line, sorted, joined. The key is the SO line
        // (falling back to the part) so the hash pins which partial-or-full SO content this shipment
        // covers — the "intersection of data" that distinguishes two shipments of the same quantities.
        var coverage = lines
            .Select(l => string.Create(CultureInfo.InvariantCulture,
                $"{(l.SalesOrderLineId is int sol ? $"L{sol}" : $"P{l.PartId}")}:{l.Quantity:0.####}"))
            .OrderBy(s => s, StringComparer.Ordinal);

        var canonical = string.Join("|", coverage);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        // 9 bytes → 12 base64url chars: ample collision resistance for a per-shipment gate, short
        // enough to render cleanly in a QR and stay readable in a log line.
        var coverageHash = Convert.ToBase64String(hash, 0, 9)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return $"{Version}.{shipmentNumber}.{coverageHash}";
    }
}
