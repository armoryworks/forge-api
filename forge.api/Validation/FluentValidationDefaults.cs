using System.Text.RegularExpressions;

using FluentValidation;

namespace Forge.Api.Validation;

/// <summary>
/// Process-wide FluentValidation settings: plain-English message templates and display
/// names taken from the validated member alone, so a rule on <c>x =&gt; x.Data.DueDate</c>
/// reads "Due date ..." rather than "'Data Due Date' ...".
/// </summary>
public static class FluentValidationDefaults
{
    private static readonly Regex WordBoundary = new(
        @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Install the Forge message templates and display-name resolver.
    /// </summary>
    public static void Apply()
    {
        ValidatorOptions.Global.LanguageManager = new ForgeLanguageManager();
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
            member is null ? null : ToDisplayName(member.Name);
    }

    /// <summary>
    /// Split a PascalCase member name into sentence-case words ("DueDate" -> "Due date",
    /// "PartId" -> "Part ID", "SKUCode" -> "SKU code").
    /// </summary>
    public static string ToDisplayName(string memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
            return memberName;

        var words = WordBoundary.Split(memberName)
            .Where(w => w.Length > 0)
            .Select((w, i) =>
            {
                if (w == "Id")
                    return "ID";
                if (w.Length > 1 && w.All(c => !char.IsLetter(c) || char.IsUpper(c)))
                    return w;
                return i == 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w.ToLowerInvariant();
            });

        return string.Join(' ', words);
    }
}
