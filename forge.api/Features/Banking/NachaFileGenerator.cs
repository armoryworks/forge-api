using System.Text;

namespace Forge.Api.Features.Banking;

/// <summary>One entry-detail line's inputs (plaintext numbers exist only inside generation).</summary>
public sealed record NachaEntry(
    string RoutingNumber,       // 9 digits incl. check digit
    string AccountNumber,       // up to 17 chars
    bool IsSavings,
    decimal Amount,             // 0 for prenotes
    string IndividualId,        // our reference, e.g. payment number (15)
    string ReceiverName);       // vendor name (22)

/// <summary>
/// Balanced-file offset (banking.nacha.balanced): our funding account, debited for the batch
/// total so the file nets to zero — required by some banks; most create the offset themselves.
/// </summary>
public sealed record NachaOffset(
    string RoutingNumber,     // 9 digits incl. check digit
    string AccountNumber,
    string CompanyName);      // receiver name on the offset entry (us)

/// <summary>The origination identity from BankingSettings (validated before generation).</summary>
public sealed record NachaOrigination(
    string ImmediateDestination,     // 9-digit bank routing
    string ImmediateDestinationName,
    string ImmediateOrigin,          // 9–10 digits (1+EIN or bank-assigned)
    string ImmediateOriginName,
    string CompanyName,              // 16 — what the vendor's statement shows
    string CompanyId,                // 10
    string OriginatingDfi,           // 8 — ODFI routing prefix (traces + batch control)
    string EntryClassCode);          // CCD | PPD

/// <summary>
/// ⚡ BANKING BOUNDARY — NACHA (ACH) flat-file writer for BANK-002 Phase A. Emits the five
/// record types as fixed 94-character lines: File Header (1), Company/Batch Header (5),
/// Entry Detail (6), Batch Control (8), File Control (9), padded with '9'-fill lines to a
/// multiple of 10 records (blocking factor). One batch per file (Phase A generates a file
/// per <c>PaymentBatch</c>). Credits only — service class 220. Prenotes are the same entry
/// shape with a prenote transaction code (23/33) and a zero amount.
///
/// Pure and deterministic: the file creation date/time is a parameter (testability; the
/// caller passes IClock-derived values).
/// </summary>
public static class NachaFileGenerator
{
    private const int RecordLength = 94;
    private const int BlockingFactor = 10;
    private const string CreditsOnlyServiceClass = "220";
    private const string MixedServiceClass = "200"; // balanced files: credits + the offset debit

    /// <summary>
    /// True when <paramref name="routing"/> is 9 digits passing the ABA checksum
    /// (3,7,1 weighting): 3(d1+d4+d7) + 7(d2+d5+d8) + 1(d3+d6+d9) ≡ 0 (mod 10).
    /// </summary>
    public static bool IsValidRoutingNumber(string? routing)
    {
        if (routing is null || routing.Length != 9 || !routing.All(char.IsAsciiDigit))
            return false;

        var d = routing.Select(c => c - '0').ToArray();
        var sum = 3 * (d[0] + d[3] + d[6]) + 7 * (d[1] + d[4] + d[7]) + (d[2] + d[5] + d[8]);
        return sum % 10 == 0;
    }

    public static string Generate(
        NachaOrigination origination,
        IReadOnlyList<NachaEntry> entries,
        DateOnly effectiveEntryDate,
        DateTimeOffset fileCreatedAt,
        bool isPrenote,
        int batchNumber,
        NachaOffset? offset = null,
        int traceSeqStart = 1)
    {
        if (entries.Count == 0)
            throw new InvalidOperationException("A NACHA file requires at least one entry.");

        // A zero-dollar prenote batch has nothing to balance — the offset only rides live batches.
        var balanced = offset is not null && !isPrenote;
        var serviceClass = balanced ? MixedServiceClass : CreditsOnlyServiceClass;

        var sb = new StringBuilder();
        var lines = new List<string>(entries.Count + 5)
        {
            FileHeader(origination, fileCreatedAt),
            BatchHeader(origination, effectiveEntryDate, isPrenote, batchNumber, serviceClass),
        };

        var entryHash = 0L;
        var totalCreditCents = 0L;
        // Trace sequences are GLOBALLY monotonic across batches (caller passes the next free
        // sequence) — a per-file restart would duplicate traces between files, and the trace is
        // the join key for bank returns (a duplicate would mis-route a return to another batch).
        var traceSeq = traceSeqStart - 1;
        var traces = new List<string>(entries.Count);

        foreach (var e in entries)
        {
            traceSeq++;
            var trace = origination.OriginatingDfi + traceSeq.ToString("D7");
            traces.Add(trace);

            // Transaction code: checking credit 22 / savings credit 32; prenote variants 23 / 33.
            var txnCode = (e.IsSavings, isPrenote) switch
            {
                (false, false) => "22",
                (false, true) => "23",
                (true, false) => "32",
                (true, true) => "33",
            };

            var cents = isPrenote ? 0L : (long)Math.Round(e.Amount * 100m, 0, MidpointRounding.AwayFromZero);
            totalCreditCents += cents;
            // Entry hash accumulates the 8-digit receiving DFI (routing without its check digit).
            entryHash += long.Parse(e.RoutingNumber[..8]);

            lines.Add(new StringBuilder(RecordLength)
                .Append('6')
                .Append(txnCode)
                .Append(e.RoutingNumber)                                  // 8-digit DFI + check digit = 9
                .Append(Left(e.AccountNumber, 17))
                .Append(cents.ToString("D10"))
                .Append(Left(e.IndividualId, 15))
                .Append(Left(e.ReceiverName, 22))
                .Append(Left(string.Empty, 2))                            // discretionary data
                .Append('0')                                              // addenda record indicator
                .Append(trace)                                            // 15
                .ToString());
        }

        var totalDebitCents = 0L;
        var entryCount = entries.Count;
        if (balanced)
        {
            // The offset: one DEBIT against our funding account for the batch total → file nets to 0.
            traceSeq++;
            var offsetTrace = origination.OriginatingDfi + traceSeq.ToString("D7");
            totalDebitCents = totalCreditCents;
            entryHash += long.Parse(offset!.RoutingNumber[..8]);
            entryCount++;
            lines.Add(new StringBuilder(RecordLength)
                .Append('6')
                .Append("27")                                             // checking debit (the offset)
                .Append(offset.RoutingNumber)
                .Append(Left(offset.AccountNumber, 17))
                .Append(totalDebitCents.ToString("D10"))
                .Append(Left("OFFSET", 15))
                .Append(Left(offset.CompanyName, 22))
                .Append(Left(string.Empty, 2))
                .Append('0')
                .Append(offsetTrace)
                .ToString());
        }

        lines.Add(BatchControl(origination, entryCount, entryHash, totalDebitCents, totalCreditCents, batchNumber, serviceClass));
        lines.Add(FileControl(entryCount, entryHash, totalDebitCents, totalCreditCents, batchCount: 1, totalRecords: lines.Count + 1));

        // Block to a multiple of 10 records with all-9 filler lines.
        while (lines.Count % BlockingFactor != 0)
            lines.Add(new string('9', RecordLength));

        foreach (var line in lines)
        {
            if (line.Length != RecordLength)
                throw new InvalidOperationException(
                    $"NACHA record length invariant violated ({line.Length} != {RecordLength}): '{line[..Math.Min(20, line.Length)]}…'");
            sb.Append(line).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Trace numbers assigned to the entries, in input order (persisted onto batch items).</summary>
    public static IReadOnlyList<string> AssignTraceNumbers(string originatingDfi, int entryCount, int traceSeqStart = 1)
        => Enumerable.Range(traceSeqStart, entryCount).Select(i => originatingDfi + i.ToString("D7")).ToList();

    private static string FileHeader(NachaOrigination o, DateTimeOffset createdAt)
        => new StringBuilder(RecordLength)
            .Append('1')
            .Append("01")                                                 // priority code
            .Append(Right(' ' + o.ImmediateDestination, 10))              // " " + 9-digit routing
            .Append(Right(o.ImmediateOrigin, 10))
            .Append(createdAt.ToString("yyMMdd"))
            .Append(createdAt.ToString("HHmm"))
            .Append('A')                                                  // file ID modifier
            .Append("094")                                                // record size
            .Append("10")                                                 // blocking factor
            .Append('1')                                                  // format code
            .Append(Left(o.ImmediateDestinationName, 23))
            .Append(Left(o.ImmediateOriginName, 23))
            .Append(Left(string.Empty, 8))                                // reference code
            .ToString();

    private static string BatchHeader(NachaOrigination o, DateOnly effectiveDate, bool isPrenote, int batchNumber, string serviceClass)
        => new StringBuilder(RecordLength)
            .Append('5')
            .Append(serviceClass)
            .Append(Left(o.CompanyName, 16))
            .Append(Left(string.Empty, 20))                               // company discretionary data
            .Append(Left(o.CompanyId, 10))
            .Append(o.EntryClassCode)                                     // CCD / PPD (3)
            .Append(Left(isPrenote ? "PRENOTE" : "PAYMENT", 10))          // company entry description
            .Append(Left(string.Empty, 6))                                // company descriptive date
            .Append(effectiveDate.ToString("yyMMdd"))
            .Append(Left(string.Empty, 3))                                // settlement date (bank fills)
            .Append('1')                                                  // originator status code
            .Append(o.OriginatingDfi)                                     // 8
            .Append(batchNumber.ToString("D7"))
            .ToString();

    private static string BatchControl(NachaOrigination o, int entryCount, long entryHash, long debitCents, long creditCents, int batchNumber, string serviceClass)
        => new StringBuilder(RecordLength)
            .Append('8')
            .Append(serviceClass)
            .Append(entryCount.ToString("D6"))
            .Append(Hash10(entryHash))
            .Append(debitCents.ToString("D12"))                           // 0 unless balanced (offset debit)
            .Append(creditCents.ToString("D12"))
            .Append(Left(o.CompanyId, 10))
            .Append(Left(string.Empty, 19))                               // message authentication code
            .Append(Left(string.Empty, 6))                                // reserved
            .Append(o.OriginatingDfi)
            .Append(batchNumber.ToString("D7"))
            .ToString();

    private static string FileControl(int entryCount, long entryHash, long debitCents, long creditCents, int batchCount, int totalRecords)
    {
        // Block count includes the filler this file will be padded to.
        var blockCount = (totalRecords + BlockingFactor - 1) / BlockingFactor;
        return new StringBuilder(RecordLength)
            .Append('9')
            .Append(batchCount.ToString("D6"))
            .Append(blockCount.ToString("D6"))
            .Append(entryCount.ToString("D8"))
            .Append(Hash10(entryHash))
            .Append(debitCents.ToString("D12"))
            .Append(creditCents.ToString("D12"))
            .Append(Left(string.Empty, 39))                               // reserved
            .ToString();
    }

    /// <summary>Entry hash keeps the rightmost 10 digits when the sum overflows the field.</summary>
    private static string Hash10(long entryHash)
    {
        var s = entryHash.ToString();
        return s.Length > 10 ? s[^10..] : s.PadLeft(10, '0');
    }

    private static string Left(string value, int width)
        => value.Length >= width ? value[..width] : value.PadRight(width, ' ');

    private static string Right(string value, int width)
        => value.Length >= width ? value[^width..] : value.PadLeft(width, ' ');
}
