using System.Text;

namespace Forge.Api.Features.Edi.X12;

/// <summary>The interchange identity for one outbound file (resolved from the trading partner).</summary>
public sealed record X12Envelope(
    string OurQualifier,
    string OurId,             // ISA sender (us)
    string TheirQualifier,
    string TheirId,           // ISA receiver (them)
    string OurGsId,
    string TheirGsId,
    int ControlNumber,        // ISA13 / GS06 / ST02 seed
    DateTimeOffset CreatedAt,
    bool Production);

/// <summary>
/// ⚡ EDI BOUNDARY — deterministic X12 writer for Forge's outbound set (855 / 856 / 810 / 997).
/// Envelopes (fixed-width ISA, GS/GE, ST/SE with computed segment counts) are written by hand —
/// the same exact-control pattern as <c>NachaFileGenerator</c> — because the envelope is where
/// format bugs cost money (padding, counts, control numbers). EDI.Net owns the genuinely hard
/// direction (inbound deserialization: delimiters, loops, conditions); the test suite parses
/// these rendered documents back to prove the two halves agree.
///
/// Delimiters: element '*', segment '~' + newline (readability; receivers ignore inter-segment
/// whitespace), component '>' — declared in ISA, so self-describing.
/// </summary>
public static class X12DocumentWriter
{
    private const char E = '*';   // element separator
    private const string SegEnd = "~\n";

    /// <summary>One transaction set body (segments between ST and SE, exclusive).</summary>
    public delegate void BodyWriter(List<string> segments);

    /// <summary>Builds a complete single-document interchange: ISA / GS / ST … SE / GE / IEA.</summary>
    public static string BuildInterchange(X12Envelope env, string transactionSet, string functionalCode, BodyWriter writeBody)
    {
        var ctrl9 = env.ControlNumber.ToString("D9");
        var ctrl4 = env.ControlNumber.ToString("D4");

        var segments = new List<string>
        {
            // ISA is fixed-width: every element padded to its exact X12 length.
            string.Join(E,
                "ISA",
                "00", new string(' ', 10),                    // auth qualifier/info
                "00", new string(' ', 10),                    // security qualifier/info
                Fixed(env.OurQualifier, 2), Fixed(env.OurId, 15),
                Fixed(env.TheirQualifier, 2), Fixed(env.TheirId, 15),
                env.CreatedAt.ToString("yyMMdd"), env.CreatedAt.ToString("HHmm"),
                "U", "00401", ctrl9,
                "0",                                          // no TA1 requested
                env.Production ? "P" : "T",
                ">"),                                         // component separator
            string.Join(E,
                "GS", functionalCode, env.OurGsId, env.TheirGsId,
                env.CreatedAt.ToString("yyyyMMdd"), env.CreatedAt.ToString("HHmm"),
                env.ControlNumber.ToString(), "X", "004010"),
            string.Join(E, "ST", transactionSet, ctrl4),
        };

        var body = new List<string>();
        writeBody(body);
        segments.AddRange(body);

        // SE counts ST + body + SE itself.
        segments.Add(string.Join(E, "SE", (body.Count + 2).ToString(), ctrl4));
        segments.Add(string.Join(E, "GE", "1", env.ControlNumber.ToString()));
        segments.Add(string.Join(E, "IEA", "1", ctrl9));

        var sb = new StringBuilder();
        foreach (var seg in segments)
            sb.Append(seg).Append(SegEnd);
        return sb.ToString();
    }

    /// <summary>855 — PO acknowledgment: BAK (acknowledge, original PO ref) + PO1 echoes + CTT.</summary>
    public static string Write855(
        X12Envelope env, string customerPoNumber, DateOnly poDate,
        IReadOnlyList<(string LineNumber, decimal Quantity, string Uom, decimal UnitPrice, string PartNumber)> lines)
        => BuildInterchange(env, "855", "PR", body =>
        {
            // BAK01 "00" original, BAK02 "AC" acknowledge with detail.
            body.Add(string.Join(E, "BAK", "00", "AC", customerPoNumber, poDate.ToString("yyyyMMdd")));
            foreach (var l in lines)
            {
                body.Add(string.Join(E, "PO1", l.LineNumber, Num(l.Quantity), l.Uom, Num(l.UnitPrice), "", "VP", l.PartNumber));
                // ACK01 "IA" item accepted.
                body.Add(string.Join(E, "ACK", "IA", Num(l.Quantity), l.Uom));
            }
            body.Add(string.Join(E, "CTT", lines.Count.ToString()));
        });

    /// <summary>856 — ASN: BSN + shipment/order hierarchy + line items.</summary>
    public static string Write856(
        X12Envelope env, string shipmentNumber, DateTimeOffset shippedAt, string? carrier, string? trackingNumber,
        string customerPoNumber,
        IReadOnlyList<(string LineNumber, decimal Quantity, string Uom, string PartNumber)> lines)
        => BuildInterchange(env, "856", "SH", body =>
        {
            body.Add(string.Join(E, "BSN", "00", shipmentNumber, shippedAt.ToString("yyyyMMdd"), shippedAt.ToString("HHmm")));
            var hlIndex = 1;
            body.Add(string.Join(E, "HL", hlIndex.ToString(), "", "S"));   // shipment level
            if (!string.IsNullOrWhiteSpace(carrier) || !string.IsNullOrWhiteSpace(trackingNumber))
                body.Add(string.Join(E, "TD5", "", "2", carrier ?? "", "", trackingNumber ?? ""));
            var shipmentHl = hlIndex;
            hlIndex++;
            body.Add(string.Join(E, "HL", hlIndex.ToString(), shipmentHl.ToString(), "O")); // order level
            body.Add(string.Join(E, "PRF", customerPoNumber));
            var orderHl = hlIndex;
            foreach (var l in lines)
            {
                hlIndex++;
                body.Add(string.Join(E, "HL", hlIndex.ToString(), orderHl.ToString(), "I")); // item level
                body.Add(string.Join(E, "LIN", l.LineNumber, "VP", l.PartNumber));
                body.Add(string.Join(E, "SN1", l.LineNumber, Num(l.Quantity), l.Uom));
            }
            body.Add(string.Join(E, "CTT", lines.Count.ToString()));
        });

    /// <summary>810 — invoice: BIG + IT1 lines + TDS total (in cents per X12 convention) + CTT.</summary>
    public static string Write810(
        X12Envelope env, string invoiceNumber, DateOnly invoiceDate, string? customerPoNumber, decimal total,
        IReadOnlyList<(string LineNumber, decimal Quantity, string Uom, decimal UnitPrice, string PartNumber)> lines)
        => BuildInterchange(env, "810", "IN", body =>
        {
            body.Add(string.Join(E, "BIG", invoiceDate.ToString("yyyyMMdd"), invoiceNumber, "", customerPoNumber ?? ""));
            foreach (var l in lines)
                body.Add(string.Join(E, "IT1", l.LineNumber, Num(l.Quantity), l.Uom, Num(l.UnitPrice), "", "VP", l.PartNumber));
            body.Add(string.Join(E, "TDS", Cents(total)));
            body.Add(string.Join(E, "CTT", lines.Count.ToString()));
        });

    /// <summary>
    /// 997 — functional acknowledgment for one received functional group:
    /// AK1 (their group) / AK9 (accepted) — the "received and parsed" attestation.
    /// </summary>
    public static string Write997(
        X12Envelope env, string ackedFunctionalCode, int ackedGroupControlNumber, int transactionSetCount, bool accepted)
        => BuildInterchange(env, "997", "FA", body =>
        {
            body.Add(string.Join(E, "AK1", ackedFunctionalCode, ackedGroupControlNumber.ToString()));
            body.Add(string.Join(E, "AK9",
                accepted ? "A" : "R",
                transactionSetCount.ToString(), transactionSetCount.ToString(),
                accepted ? transactionSetCount.ToString() : "0"));
        });

    private static string Fixed(string value, int width)
        => value.Length >= width ? value[..width] : value.PadRight(width, ' ');

    /// <summary>X12 numeric: invariant, no thousands separators, trailing zeros trimmed.</summary>
    private static string Num(decimal value)
    {
        var s = value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return s;
    }

    /// <summary>TDS amounts are implied-decimal N2 (cents).</summary>
    private static string Cents(decimal value)
        => ((long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero)).ToString();
}
