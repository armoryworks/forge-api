using AwesomeAssertions;
using Moq;

using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class SalesOrderAcceptancePdfDocumentTests
{
    [Fact]
    public void Pdf_ShowsTheLinesTotalAndSignatureBlock()
    {
        var html = SalesOrderAcceptancePdfDocument.Build(
            "Harbor Gear Works", "SO-7001", "Buyer Co", "Net 30. Prices firm for 30 days.",
            [new("Bracket", 4m, 12.5m), new("Shaft", 1m, 100m)],
            MoneyFormat.For(null, "USD")).Html;

        html.Should().Contain("Sales Order Acceptance").And.Contain("Order SO-7001  ·  Buyer Co");
        html.Should().Contain("$50.00").And.Contain("$150.00").And.Contain("Net 30. Prices firm for 30 days.");
        html.Should().Contain("By signing below, the customer accepts the terms of this Sales Order and authorizes production.");
        html.Should().Contain("Customer signature").And.Contain(">Date<");
    }

    [Fact]
    public void Pdf_EncodesEveryValueAUserTyped_AndLabelsTheFooterWithTheOrderNumber()
    {
        var html = SalesOrderAcceptancePdfDocument.Build(
            "Harbor <Gear> Works", "SO-7002", "<img src=x onerror=alert(1)>", "<script>alert(1)</script>",
            [new("Bracket <b>bold</b>", 1m, 1m)]).Html;

        html.Should().NotContain("<img").And.NotContain("<script").And.NotContain("<b>");
        html.Should().Contain("&lt;img src=x onerror=alert(1)&gt;").And.Contain("Bracket &lt;b&gt;bold&lt;/b&gt;")
            .And.Contain("Harbor &lt;Gear&gt; Works").And.Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.Should().Contain("<div class=\"page-footer\">SO-7002 &middot; Page ");
    }

    [Fact]
    public async Task SendForSignature_PrintsTheAcceptanceThroughTheRenderer_AndUploadsIt()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Buyer Co" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var order = new SalesOrder { OrderNumber = "SO-7003", CustomerId = customer.Id, Status = SalesOrderStatus.Draft };
        order.Lines.Add(new SalesOrderLine { Description = "Bracket", Quantity = 2m, UnitPrice = 10m, LineNumber = 1 });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        byte[]? uploaded = null;
        var signing = new Mock<IDocumentSigningService>();
        signing.Setup(s => s.CreateTemplateFromPdfAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, byte[] bytes, CancellationToken _) => uploaded = bytes)
            .ReturnsAsync(11);
        signing.Setup(s => s.CreateSubmissionAsync(11, "ap@buyer.test", "Pat Buyer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentSigningSubmission(22, "https://sign.example/s/22"));
        var settings = new Mock<ISystemSettingRepository>();
        settings.Setup(s => s.FindByKeyAsync("company.name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "company.name", Value = "Harbor Gear Works" });
        var renderer = new FakeHtmlPdfRenderer();

        await new SendSalesOrderForSignatureHandler(db, signing.Object, settings.Object, new SystemClock(), renderer)
            .Handle(new SendSalesOrderForSignatureCommand(order.Id, "ap@buyer.test", "Pat Buyer"), CancellationToken.None);

        var printed = renderer.Documents.Should().ContainSingle().Subject;
        printed.Html.Should().Contain("Harbor Gear Works").And.Contain("Order SO-7003  ·  Buyer Co").And.Contain("$20.00");
        uploaded.Should().NotBeNull().And.StartWith("%PDF-"u8.ToArray());
    }
}
