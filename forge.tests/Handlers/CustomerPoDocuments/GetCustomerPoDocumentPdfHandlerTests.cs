using FluentAssertions;
using Moq;
using QuestPDF.Infrastructure;

using Forge.Api.Features.CustomerPoDocuments;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerPoDocuments;

/// <summary>
/// S4a — PDF contract test (same shape as CustomerStatementContractTests):
/// the QuestPDF document composes against a representative live SO without
/// throwing and yields a non-empty byte[].
/// </summary>
public class GetCustomerPoDocumentPdfHandlerTests
{
    static GetCustomerPoDocumentPdfHandlerTests()
    {
        // QuestPDF requires an explicit license declaration in test contexts.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task PdfHandler_ProducesNonEmptyPdf_FromLiveSalesOrder()
    {
        using var db = TestDbContextFactory.Create();

        var customer = new Customer
        {
            Name = "PDF Contract Co",
            Email = "po@pdfco.example",
            Phone = "+1-555-0101",
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var address = new CustomerAddress
        {
            CustomerId = customer.Id,
            Label = "Dock 1",
            Line1 = "42 Shipping Lane",
            City = "Peoria",
            State = "IL",
            PostalCode = "61602",
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var order = new SalesOrder
        {
            OrderNumber = "SO-88001",
            CustomerId = customer.Id,
            ShippingAddressId = address.Id,
            CustomerPO = "PO-EXT-123",
            Notes = "Deliver to dock 1",
            TaxRate = 0.0875m,
        };
        order.Lines.Add(new SalesOrderLine { Description = "Bracket, welded", Quantity = 12, UnitPrice = 18.50m, LineNumber = 1 });
        order.Lines.Add(new SalesOrderLine { Description = "Machined shaft", Quantity = 3.5m, UnitPrice = 240m, LineNumber = 2 });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        db.CustomerPoDocuments.Add(new CustomerPoDocument
        {
            SalesOrderId = order.Id,
            DocumentNumber = "CPO-00001",
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var settings = new Mock<ISystemSettingRepository>();
        settings.Setup(s => s.FindByKeyAsync("company_name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "company_name", Value = "Armory Works Test" });

        var handler = new GetCustomerPoDocumentPdfHandler(db, settings.Object);

        var pdf = await handler.Handle(
            new GetCustomerPoDocumentPdfQuery(order.Id), CancellationToken.None);

        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(0, "the document must compose without throwing");
    }

    [Fact]
    public async Task PdfHandler_NoDocumentForOrder_ThrowsKeyNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var settings = new Mock<ISystemSettingRepository>();
        var handler = new GetCustomerPoDocumentPdfHandler(db, settings.Object);

        var act = () => handler.Handle(
            new GetCustomerPoDocumentPdfQuery(31337), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*31337*");
    }
}
