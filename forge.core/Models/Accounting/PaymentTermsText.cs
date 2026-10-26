namespace Forge.Core.Models.Accounting;

/// <summary>
/// Matches free-text terms ("Net 30", "2/10 Net 30", "net30") to a terms code or name, ignoring case, spaces
/// and punctuation, the same way the data/0060 backfill matches vendor terms.
/// </summary>
public static class PaymentTermsText
{
    public static string Normalize(string? value)
        => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    public static bool Matches(string? text, string code, string name)
    {
        var normalized = Normalize(text);
        return normalized.Length > 0 && (normalized == Normalize(code) || normalized == Normalize(name));
    }
}
