using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Forge.Api.Services;

/// <summary>
/// Font families for generated PDFs. QuestPDF only falls back to fonts that are listed on the text style, so
/// without this a customer or vendor name in Chinese, Japanese, Korean or Arabic, or an emoji, prints as empty
/// boxes. The fallbacks are the Noto CJK, Amiri and Noto Color Emoji fonts installed in the API image; where a
/// family is missing (a developer machine, a test run) it is skipped.
/// </summary>
public static class PdfFonts
{
    private static readonly string[] ScriptFallbacks = ["Noto Sans CJK JP", "Amiri", "Noto Color Emoji"];

    /// <summary>Uses <paramref name="primary"/> (QuestPDF's bundled Lato by default) with the script fallbacks.</summary>
    public static TextStyle WithScriptFallbacks(this TextStyle style, string primary = Fonts.Lato)
        => style.FontFamily([primary, .. ScriptFallbacks]);
}
