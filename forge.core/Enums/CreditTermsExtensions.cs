namespace Forge.Core.Enums;

public static class CreditTermsExtensions
{
    /// <summary>The terms the API accepts, in the order a picker lists them.</summary>
    public const string AllowedValuesMessage = "Credit terms must be one of: DueOnReceipt, Net15, Net30, Net45, Net60, Net90.";

    /// <summary>Days from the document date to the due date under these terms (DueOnReceipt is 0).</summary>
    public static int NetDays(this CreditTerms terms) => terms switch
    {
        CreditTerms.Net15 => 15,
        CreditTerms.Net30 => 30,
        CreditTerms.Net45 => 45,
        CreditTerms.Net60 => 60,
        CreditTerms.Net90 => 90,
        _ => 0,
    };

    /// <summary>The terms whose net days are exactly <paramref name="days"/>, or null when none match.</summary>
    public static CreditTerms? FromNetDays(int days) => days switch
    {
        0 => CreditTerms.DueOnReceipt,
        15 => CreditTerms.Net15,
        30 => CreditTerms.Net30,
        45 => CreditTerms.Net45,
        60 => CreditTerms.Net60,
        90 => CreditTerms.Net90,
        _ => null,
    };

    /// <summary>
    /// Reads enum names and the spellings people type ("Net 30", "net30", "NET-30", "Due on receipt"),
    /// ignoring case, spaces and punctuation. Numbers alone are rejected, so "3" never becomes Net45.
    /// </summary>
    public static bool TryParseLenient(string? value, out CreditTerms terms)
    {
        var normalized = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray())
            .ToLowerInvariant();

        CreditTerms? parsed = normalized switch
        {
            "dueonreceipt" => CreditTerms.DueOnReceipt,
            "net15" => CreditTerms.Net15,
            "net30" => CreditTerms.Net30,
            "net45" => CreditTerms.Net45,
            "net60" => CreditTerms.Net60,
            "net90" => CreditTerms.Net90,
            _ => null,
        };

        terms = parsed ?? default;
        return parsed is not null;
    }

    /// <summary>
    /// True when <paramref name="dueDate"/> falls exactly <see cref="NetDays"/> calendar days after
    /// <paramref name="documentDate"/>, counting days in the document date's own offset.
    /// </summary>
    public static bool MatchesDueDate(this CreditTerms terms, DateTimeOffset documentDate, DateTimeOffset dueDate)
        => (dueDate.ToOffset(documentDate.Offset).Date - documentDate.Date).Days == terms.NetDays();
}
