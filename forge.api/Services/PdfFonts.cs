using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Forge.Api.Services;

/// <summary>
/// Font families for generated PDFs. A customer or vendor name in Chinese, Japanese, Korean or Arabic, or an
/// emoji, needs a font with those glyphs or it prints as empty boxes; the API image installs the Noto CJK, Amiri
/// and Noto Color Emoji fonts, and listing them here fixes the order QuestPDF tries them in. Where a family is
/// missing (a developer machine, a test run) it is skipped.
/// </summary>
public static class PdfFonts
{
    private static readonly string[] ScriptFallbacks = ["Noto Sans CJK JP", "Amiri", "Noto Color Emoji"];

    /// <summary>Uses <paramref name="primary"/> (QuestPDF's bundled Lato by default) with the script fallbacks.</summary>
    public static TextStyle WithScriptFallbacks(this TextStyle style, string primary = Fonts.Lato)
        => style.FontFamily([primary, .. ScriptFallbacks]);
}
