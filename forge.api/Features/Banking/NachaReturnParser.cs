namespace Forge.Api.Features.Banking;

/// <summary>One returned/corrected entry from a bank returns file.</summary>
public sealed record NachaReturnEntry(
    string OriginalTraceNumber,   // 15 — joins back to PaymentBatchItem.TraceNumber
    string ReasonCode,            // R-code (return) or C-code (NOC)
    bool IsNotificationOfChange,  // addenda 98 (NOC) vs 99 (return)
    decimal Amount,               // from the preceding entry-detail record
    string? CorrectedData);       // NOC only — the bank's corrected account info

/// <summary>
/// ⚡ BANKING BOUNDARY — parser for bank ACH return / NOC files (BANK-002 Phase C). Returns
/// come back as NACHA-STANDARD files — an entry-detail (6) record echoing the original entry,
/// followed by a type-7 addenda: type 99 (return — R-code + the ORIGINAL trace number) or
/// type 98 (notification of change — C-code + corrected data). Because the format is the NACHA
/// standard, this parser is bank-agnostic: any of the ~5,000 originating banks' return files
/// parse identically. Tolerant of blocking filler and blank lines.
/// </summary>
public static class NachaReturnParser
{
    public static IReadOnlyList<NachaReturnEntry> Parse(string contents)
    {
        var entries = new List<NachaReturnEntry>();
        decimal pendingAmount = 0m;

        foreach (var raw in contents.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 94)
                continue;

            switch (line[0])
            {
                case '6':
                    // Entry detail: amount at positions 29..39 (10, implied cents).
                    pendingAmount = long.TryParse(line.Substring(29, 10), out var cents) ? cents / 100m : 0m;
                    break;

                case '7':
                    var addendaType = line.Substring(1, 2);
                    if (addendaType is not ("99" or "98"))
                        break;

                    // Addenda 98/99 layout: reason/change code at 3..6, original trace at 6..21,
                    // corrected data (NOC) at 35..64.
                    var reasonCode = line.Substring(3, 3).Trim();
                    var originalTrace = line.Substring(6, 15).Trim();
                    var isNoc = addendaType == "98";

                    entries.Add(new NachaReturnEntry(
                        originalTrace,
                        reasonCode,
                        isNoc,
                        pendingAmount,
                        isNoc ? line.Substring(35, 29).Trim() : null));
                    pendingAmount = 0m;
                    break;
            }
        }

        return entries;
    }

    /// <summary>Human copy for the common credit-origination R/C codes (unknown codes pass through).</summary>
    public static string Describe(string code) => code.ToUpperInvariant() switch
    {
        "R01" => "Insufficient funds",
        "R02" => "Account closed",
        "R03" => "No account / unable to locate",
        "R04" => "Invalid account number",
        "R05" => "Unauthorized debit",
        "R06" => "Returned per ODFI request",
        "R07" => "Authorization revoked",
        "R08" => "Payment stopped",
        "R09" => "Uncollected funds",
        "R10" => "Customer advises not authorized",
        "R12" => "Account sold to another DFI",
        "R13" => "Invalid ACH routing number",
        "R16" => "Account frozen",
        "R17" => "File record edit criteria",
        "R20" => "Non-transaction account",
        "R29" => "Corporate customer advises not authorized",
        "C01" => "Corrected account number",
        "C02" => "Corrected routing number",
        "C03" => "Corrected routing and account number",
        "C05" => "Corrected transaction code",
        "C06" => "Corrected account number and transaction code",
        var other => other,
    };
}
