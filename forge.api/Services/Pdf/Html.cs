using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace Forge.Api.Services.Pdf;

/// <summary>
/// Markup that is safe to place in a document. Text becomes markup only by being encoded:
/// <c>Html.Of($"&lt;td&gt;{line.Description}&lt;/td&gt;")</c> encodes every interpolated value except another
/// <see cref="Html"/>, so a name or note typed by a user can never add tags to a printed document.
/// </summary>
public readonly record struct Html(string Value)
{
    public static Html Empty => new(string.Empty);

    public static Html Text(string? text) => new(WebUtility.HtmlEncode(text ?? string.Empty));

    public static Html Of(HtmlInterpolation markup) => new(markup.ToString());

    public static Html Join(IEnumerable<Html> parts) => new(string.Concat(parts.Select(p => p.Value)));

    public static Html Image(byte[] bytes, string contentType = "image/png", string? cssClass = null)
        => Of($"<img class=\"{cssClass}\" alt=\"\" src=\"data:{contentType};base64,{Convert.ToBase64String(bytes)}\">");

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>The interpolated-string handler behind <see cref="Html.Of"/>: literals pass through, values are encoded.</summary>
[InterpolatedStringHandler]
public readonly ref struct HtmlInterpolation
{
    private readonly StringBuilder _markup;

    public HtmlInterpolation(int literalLength, int formattedCount)
    {
        _markup = new StringBuilder(literalLength + formattedCount * 16);
    }

    public void AppendLiteral(string literal) => _markup.Append(literal);

    public void AppendFormatted(Html html) => _markup.Append(html.Value);

    public void AppendFormatted(IEnumerable<Html> parts)
    {
        foreach (var part in parts)
            _markup.Append(part.Value);
    }

    public void AppendFormatted(string? text) => _markup.Append(WebUtility.HtmlEncode(text));

    public void AppendFormatted<T>(T value)
    {
        switch (value)
        {
            case Html html:
                AppendFormatted(html);
                break;
            case IEnumerable<Html> parts:
                AppendFormatted(parts);
                break;
            default:
                _markup.Append(WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture)));
                break;
        }
    }

    public void AppendFormatted<T>(T value, string? format) where T : IFormattable
        => _markup.Append(WebUtility.HtmlEncode(value.ToString(format, CultureInfo.InvariantCulture)));

    public void AppendFormatted<T>(T? value, string? format) where T : struct, IFormattable
        => _markup.Append(WebUtility.HtmlEncode(value?.ToString(format, CultureInfo.InvariantCulture)));

    public override string ToString() => _markup.ToString();
}
