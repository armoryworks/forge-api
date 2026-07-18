using System.Linq;

namespace Forge.Api.Features.Barcodes;

/// <summary>
/// GS1 GTIN math — the check-digit algorithm, building a GTIN-13 from a licensed company prefix + an
/// item reference, and validating a pasted GTIN. Pure and static so it's directly unit-testable. This is
/// what makes a self-issued code globally unique: the company prefix is licensed from GS1, so no two
/// companies can mint the same GTIN.
/// </summary>
public static class Gs1
{
    /// <summary>Setting keys for the (optional) GS1 licence configuration.</summary>
    public const string CompanyPrefixKey = "barcode.gs1_company_prefix";
    public const string NextItemRefKey = "barcode.gs1_next_item_ref";

    public static bool IsAllDigits(string? s) => !string.IsNullOrEmpty(s) && s.All(char.IsDigit);

    /// <summary>GS1 mod-10 check digit over a body string (all digits except the check digit).</summary>
    public static int CalculateCheckDigit(string bodyDigits)
    {
        if (!IsAllDigits(bodyDigits))
            throw new ArgumentException("GTIN body must be all digits.", nameof(bodyDigits));

        var sum = 0;
        for (var i = 0; i < bodyDigits.Length; i++)
        {
            // Weight alternates 3,1,3,1… starting from the rightmost body digit.
            var digit = bodyDigits[bodyDigits.Length - 1 - i] - '0';
            sum += digit * (i % 2 == 0 ? 3 : 1);
        }
        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// Build a GTIN-13 from a licensed company prefix + item reference. Prefix + item reference occupy the
    /// first 12 digits; the 13th is the check digit. A shorter prefix leaves more item-reference capacity.
    /// </summary>
    public static string BuildGtin13(string companyPrefix, long itemReference)
    {
        if (!IsAllDigits(companyPrefix))
            throw new ArgumentException("GS1 company prefix must be digits only.", nameof(companyPrefix));
        if (companyPrefix.Length is < 6 or > 11)
            throw new ArgumentException("GS1 company prefix must be 6–11 digits for a GTIN-13.", nameof(companyPrefix));
        if (itemReference < 0)
            throw new ArgumentOutOfRangeException(nameof(itemReference));

        var itemWidth = 12 - companyPrefix.Length;
        var itemRefStr = itemReference.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (itemRefStr.Length > itemWidth)
            throw new InvalidOperationException(
                $"Item reference {itemReference} exceeds the {itemWidth}-digit capacity of prefix {companyPrefix}. The prefix's GTIN allocation is exhausted.");

        var body = companyPrefix + itemRefStr.PadLeft(itemWidth, '0');
        return body + CalculateCheckDigit(body);
    }

    /// <summary>Valid GTIN = 8/12/13/14 digits with a correct check digit.</summary>
    public static bool IsValidGtin(string? gtin)
    {
        if (!IsAllDigits(gtin) || gtin!.Length is not (8 or 12 or 13 or 14))
            return false;
        var body = gtin[..^1];
        var check = gtin[^1] - '0';
        return CalculateCheckDigit(body) == check;
    }
}
