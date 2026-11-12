using AwesomeAssertions;

using Forge.Api.Features.Shipments;
using Forge.Api.Services;
using Forge.Core.Entities;

namespace Forge.Tests.Handlers.Shipments;

public class ShipmentDocumentHtmlTests
{
    private static Shipment Shipment(string notes = "Leave at dock 4")
    {
        var customer = new Customer { Name = "Harbor <b>Gear</b>", CompanyName = "Harbor <script>alert(1)</script> Works" };
        var order = new SalesOrder { Id = 7, OrderNumber = "SO-0007", Customer = customer, CustomerId = 1 };
        var part = new Part { Id = 3, PartNumber = "FG-100", Description = "Bracket <b>bold</b>" };
        return new Shipment
        {
            Id = 11,
            ShipmentNumber = "SHP-0011",
            SalesOrderId = order.Id,
            SalesOrder = order,
            Carrier = "UPS",
            TrackingNumber = "1Z999",
            Notes = notes,
            ShippingAddress = new CustomerAddress { Line1 = "9 Pier <i>St</i>", City = "Boise", State = "ID", PostalCode = "83702", Country = "US" },
            Lines = [new ShipmentLine { Id = 1, PartId = part.Id, Part = part, Quantity = 4m }],
            Packages = [new ShipmentPackage { TrackingNumber = "1Z999", Weight = 12.5m, Length = 10m, Width = 8m, Height = 6m }],
        };
    }

    [Fact]
    public void Packing_slip_encodes_what_users_typed()
    {
        var html = PackingSlipPdfDocument.Build(Shipment("</div><script>fetch('x')</script>"), "Acme <b>Co</b>").Html;

        html.Should().NotContain("<script").And.NotContain("<b>").And.NotContain("<i>");
        html.Should().Contain("Harbor &lt;script&gt;alert(1)&lt;/script&gt; Works")
            .And.Contain("Bracket &lt;b&gt;bold&lt;/b&gt;")
            .And.Contain("Acme &lt;b&gt;Co&lt;/b&gt;")
            .And.Contain("&lt;/div&gt;&lt;script&gt;");
    }

    [Fact]
    public void Packing_slip_footer_carries_the_shipment_number_and_embeds_its_qr_codes()
    {
        var html = PackingSlipPdfDocument.Build(Shipment(), "Acme").Html;

        html.Should().Contain("<div class=\"page-footer\">SHP-0011 &middot; Page ");
        html.Should().Contain("src=\"data:image/png;base64,");
        html.Should().Contain("PACKING SLIP").And.Contain("#SHP-0011").And.Contain("SCAN TO SHIP").And.Contain("SO-0007")
            .And.Contain("Tracking: 1Z999").And.Contain("Total Items: 4").And.Contain("Received By");
    }

    [Fact]
    public void Bill_of_lading_encodes_what_users_typed()
    {
        var html = BillOfLadingPdfDocument.Build(Shipment("<b>fragile</b>"), "Acme <script>x</script>", "1 Main <b>St</b>", "555").Html;

        html.Should().NotContain("<script").And.NotContain("<b>");
        html.Should().Contain("&lt;b&gt;fragile&lt;/b&gt;")
            .And.Contain("Acme &lt;script&gt;x&lt;/script&gt;")
            .And.Contain("1 Main &lt;b&gt;St&lt;/b&gt;")
            .And.Contain("Bracket &lt;b&gt;bold&lt;/b&gt;");
    }

    [Fact]
    public void Bill_of_lading_footer_carries_the_bill_number()
    {
        var html = BillOfLadingPdfDocument.Build(Shipment(), "Acme").Html;

        html.Should().Contain("<div class=\"page-footer\">BOL-SHP-0011 &middot; Page ");
        System.Net.WebUtility.HtmlDecode(html).Should().Contain("BILL OF LADING").And.Contain("SPECIAL INSTRUCTIONS:").And.Contain("12.5 lbs")
            .And.Contain("10 × 8 × 6 in").And.Contain("This is to certify");
    }

    [Fact]
    public void Ship_document_encodes_what_users_typed()
    {
        var html = ShipDocumentPdfDocument.Build(
            Shipment(), QrCodeRenderer.Png("label"), "Acme <b>Co</b>", "1 Main <script>St</script>", null, CarrierBadge.For("ups")).Html;

        html.Should().NotContain("<script").And.NotContain("<b>");
        html.Should().Contain("Acme &lt;b&gt;Co&lt;/b&gt;")
            .And.Contain("1 Main &lt;script&gt;St&lt;/script&gt;")
            .And.Contain("Ship to: Harbor &lt;b&gt;Gear&lt;/b&gt;");
    }

    [Fact]
    public void Ship_document_footer_carries_the_shipment_number_and_embeds_the_label_and_qr()
    {
        var html = ShipDocumentPdfDocument.Build(
            Shipment(), QrCodeRenderer.Png("label"), "Acme", null, null, CarrierBadge.For("fedex")).Html;

        html.Should().Contain("<div class=\"page-footer\">SHP-0011 &middot; Page ");
        html.Should().Contain("<body class=\"landscape\">");
        html.Should().Contain("<img class=\"label\" alt=\"\" src=\"data:image/png;base64,")
            .And.Contain("<img class=\"qr\" alt=\"\" src=\"data:image/png;base64,");
        html.Should().Contain("FEDEX").And.Contain("background: #4D148C; border-left: 10pt solid #FF6600;");
    }

    [Fact]
    public void Ship_document_refuses_a_badge_colour_that_is_not_a_hex_value()
    {
        var html = ShipDocumentPdfDocument.Build(
            Shipment(), QrCodeRenderer.Png("label"), "Acme", null, null,
            new CarrierBadgeStyle("X", "red; background: url(http://x)", "#fff")).Html;

        html.Should().NotContain("url(").And.Contain("background: #37474F; border-left: 10pt solid #fff;");
    }
}
