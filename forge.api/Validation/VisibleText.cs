using System.Globalization;
using System.Text;

namespace Forge.Api.Validation;

/// <summary>
/// Shared check for free-text identity fields that must show something on screen.
/// FluentValidation's <c>NotEmpty</c> treats ASCII whitespace as empty, but Unicode
/// whitespace and zero-width format characters (ZWSP, ZWNJ, ZWJ, BOM) pass it.
/// </summary>
public static class VisibleText
{
    /// <summary>
    /// True when <paramref name="value"/> holds at least one character that is not
    /// whitespace, a format character or a control character.
    /// </summary>
    public static bool HasVisibleContent(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) continue;
            var category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
            if (category == UnicodeCategory.Format || category == UnicodeCategory.Control) continue;
            return true;
        }
        return false;
    }
}
