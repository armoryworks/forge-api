using AwesomeAssertions;

using Forge.Api.Services.Pdf;

namespace Forge.Tests.Services.Pdf;

public class HtmlTests
{
    [Fact]
    public void Interpolated_text_is_encoded_and_literals_are_not()
    {
        var name = "<script>alert('x')</script> & Sons \"Ltd\"";

        var html = Html.Of($"<td class=\"n\">{name}</td>");

        html.Value.Should().Be("<td class=\"n\">&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; Sons &quot;Ltd&quot;</td>");
    }

    [Fact]
    public void Markup_passes_through_once()
    {
        var cell = Html.Of($"<td>{"a < b"}</td>");

        Html.Of($"<tr>{cell}</tr>").Value.Should().Be("<tr><td>a &lt; b</td></tr>");
    }

    [Fact]
    public void A_sequence_of_markup_is_concatenated_whatever_its_static_type()
    {
        var list = new List<Html> { Html.Of($"<li>{"1 & 2"}</li>"), Html.Of($"<li>{3}</li>") };
        IEnumerable<Html> lazy = list.Select(item => item);

        Html.Of($"<ul>{list}</ul>").Value.Should().Be("<ul><li>1 &amp; 2</li><li>3</li></ul>");
        Html.Of($"<ul>{lazy}</ul>").Value.Should().Be("<ul><li>1 &amp; 2</li><li>3</li></ul>");
        Html.Join(list).Value.Should().Be("<li>1 &amp; 2</li><li>3</li>");
    }

    [Fact]
    public void Numbers_and_dates_format_with_the_invariant_culture()
    {
        var date = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset? none = null;

        Html.Of($"{1234.5m}|{date:MMM dd, yyyy}|{none:MM/dd/yyyy}|{(string?)null}").Value.Should().Be("1234.5|Nov 03, 2026||");
    }

    [Fact]
    public void An_image_is_embedded_as_inline_data()
    {
        var image = Html.Image([1, 2, 3], "image/png", "qr");

        image.Value.Should().Be("<img class=\"qr\" alt=\"\" src=\"data:image/png;base64,AQID\">");
    }

    [Fact]
    public void A_document_carries_its_own_styles_footer_and_orientation()
    {
        var document = PdfHtml.Document("Count <sheet>", Html.Of($"<p>{"body"}</p>"), "PI-7 & 8", landscape: true, styles: ".sheet { color: red; }");

        document.Html.Should().Contain("<title>Count &lt;sheet&gt;</title>")
            .And.Contain(".sheet { color: red; }</style>")
            .And.Contain("<body class=\"landscape\">")
            .And.Contain("<div class=\"page-footer\">PI-7 &amp; 8 &middot; Page <span class=\"page\"></span> of <span class=\"pages\"></span></div>")
            .And.Contain("<p>body</p>");
    }

    [Fact]
    public void Text_and_the_default_value_are_safe()
    {
        Html.Text("5 > 3").Value.Should().Be("5 &gt; 3");
        Html.Text(null).Value.Should().BeEmpty();
        Html.Of($"[{default(Html)}]").Value.Should().Be("[]");
        default(Html).ToString().Should().BeEmpty();
    }
}
