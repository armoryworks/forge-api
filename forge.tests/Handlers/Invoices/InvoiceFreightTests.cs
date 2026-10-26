using FluentAssertions;
using FluentValidation;
using Moq;

using Forge.Api.Features.Freight;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class InvoiceFreightTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly DateTimeOffset _invoiceDate = new(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);
    private Invoice? _added;

    private async Task<Customer> SeedAsync(bool freightTaxable, bool exempt = false)
    {
        var customer = new Customer { Name = "Acme", IsTaxExempt = exempt };
        _db.Customers.Add(customer);
        _db.SalesTaxRates.Add(new SalesTaxRate
        {
            Name = "Default", Code = "DEF", Rate = 0.0725m, IsDefault = true, IsActive = true,
            EffectiveFrom = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), FreightTaxable = freightTaxable,
        });
        await _db.SaveChangesAsync();

        _customerRepo.Setup(r => r.FindAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("INV-1");
        _invoiceRepo.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((i, _) => _added = i)
            .Returns(Task.CompletedTask);
        return customer;
    }

    private CreateInvoiceCommand Command(int customerId, params CreateInvoiceLineModel[] lines)
        => new(customerId, null, null, _invoiceDate, _invoiceDate.AddDays(30), null, 0.0725m, null, [.. lines]);

    private Task Create(CreateInvoiceCommand command)
        => new CreateInvoiceHandler(_invoiceRepo.Object, _customerRepo.Object, _db).Handle(command, CancellationToken.None);

    [Fact]
    public async Task FreightLine_StartsNonTaxable_WhenTheJurisdictionDoesNotTaxFreight()
    {
        var customer = await SeedAsync(freightTaxable: false);

        await Create(Command(customer.Id,
            new CreateInvoiceLineModel(null, "Goods", 1, 9_685m),
            new CreateInvoiceLineModel(null, "", 1, 300m, LineType: DocumentLineType.Freight)));

        var freight = _added!.Lines.Single(l => l.IsFreight);
        freight.TaxCode.Should().Be(InvoiceLine.NonTaxableCode);
        freight.Description.Should().Be(FreightLines.DefaultDescription);
        _added.TaxAmount.Should().Be(702.16m);
        _added.FreightSubtotal.Should().Be(300m);
        _added.SalesTaxRateId.Should().NotBeNull();
    }

    [Fact]
    public async Task FreightLine_StartsTaxable_WhenTheJurisdictionTaxesFreight()
    {
        var customer = await SeedAsync(freightTaxable: true);

        await Create(Command(customer.Id,
            new CreateInvoiceLineModel(null, "Goods", 1, 9_685m),
            new CreateInvoiceLineModel(null, "Freight", 1, 300m, LineType: DocumentLineType.Freight)));

        _added!.Lines.Single(l => l.IsFreight).TaxCode.Should().Be(FreightLines.TaxableCode);
        _added.TaxAmount.Should().Be(723.91m);
    }

    [Fact]
    public async Task FreightLine_StaysNonTaxable_ForAnExemptCustomer()
    {
        var customer = await SeedAsync(freightTaxable: true, exempt: true);

        await Create(Command(customer.Id,
            new CreateInvoiceLineModel(null, "Freight", 1, 300m, LineType: DocumentLineType.Freight)));

        _added!.Lines.Single().TaxCode.Should().Be(InvoiceLine.NonTaxableCode);
    }

    [Fact]
    public async Task FreightLine_ExplicitTaxableWins()
    {
        var customer = await SeedAsync(freightTaxable: false);

        await Create(Command(customer.Id,
            new CreateInvoiceLineModel(null, "Freight", 1, 300m, Taxable: true, LineType: DocumentLineType.Freight)));

        _added!.Lines.Single().TaxCode.Should().Be(FreightLines.TaxableCode);
    }

    [Fact]
    public void Validator_RejectsAFreightLineWithAPart()
    {
        var result = new CreateInvoiceValidator().Validate(Command(1,
            new CreateInvoiceLineModel(5, "Freight", 1, 10m, LineType: DocumentLineType.Freight)));

        result.Errors.Should().Contain(e => e.ErrorCode == FreightLines.PartCode);
    }

    [Fact]
    public void Validator_AllowsABlankFreightDescription()
    {
        var result = new CreateInvoiceValidator().Validate(Command(1,
            new CreateInvoiceLineModel(null, "", 1, 10m, LineType: DocumentLineType.Freight)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task SalesOrderFreight_BillsOnce()
    {
        var customer = await SeedAsync(freightTaxable: false);
        var order = new SalesOrder { OrderNumber = "SO-1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        var freightLine = new SalesOrderLine { Description = "Freight", Quantity = 1, UnitPrice = 40m, LineNumber = 1, LineType = DocumentLineType.Freight };
        order.Lines.Add(freightLine);
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        _db.Invoices.Add(new Invoice
        {
            InvoiceNumber = "INV-0", CustomerId = customer.Id, SalesOrderId = order.Id, Status = InvoiceStatus.Sent,
            Lines = [new InvoiceLine { Description = "Freight", Quantity = 1, UnitPrice = 40m, LineNumber = 1, LineType = DocumentLineType.Freight, SalesOrderLineId = freightLine.Id }],
        });
        await _db.SaveChangesAsync();

        var act = () => Create(Command(customer.Id,
            new CreateInvoiceLineModel(null, "Freight", 1, 40m, SalesOrderLineId: freightLine.Id, LineType: DocumentLineType.Freight)) with { SalesOrderId = order.Id });

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == FreightLines.SoFreightAlreadyBilledCode);
    }

    private static InvoiceSourceShipmentResponseModel Shipment(decimal? charge)
        => new(1, "SHP-1", 9, "SO-9", null,
            [new InvoiceSourceShipmentLineResponseModel(3, "P-3", "Widget", 2, 10m)], charge);

    private static readonly InvoiceSourceShipmentLineResponseModel QuotedFreight =
        new(null, null, "Freight", 1, 25m, false, 77, DocumentLineType.Freight);

    [Fact]
    public void Sources_AddTheShipmentFreightCharge_WhenTheOrderQuotesNoFreight()
    {
        var result = GetInvoiceSourcesHandler.WithFreight(Shipment(18.5m), (false, []));

        result.Lines.Should().HaveCount(2);
        result.Lines[1].Should().Match<InvoiceSourceShipmentLineResponseModel>(l =>
            l.LineType == DocumentLineType.Freight && l.UnitPrice == 18.5m && l.PartId == null);
        result.Warnings.Should().BeNull();
    }

    [Fact]
    public void Sources_BillQuotedFreightInsteadOfTheShipmentCharge_AndWarn()
    {
        var result = GetInvoiceSourcesHandler.WithFreight(Shipment(18.5m), (true, [QuotedFreight]));

        result.Lines.Where(l => l.LineType == DocumentLineType.Freight).Should().ContainSingle()
            .Which.SalesOrderLineId.Should().Be(77);
        result.Warnings.Should().Equal(FreightLines.SoFreightAlreadyBilledCode);
    }

    [Fact]
    public void Sources_DoNotRebillFreight_WhenTheOrderFreightWasBilled()
    {
        var result = GetInvoiceSourcesHandler.WithFreight(Shipment(18.5m), (true, []));

        result.Lines.Should().ContainSingle();
        result.Warnings.Should().Equal(FreightLines.SoFreightAlreadyBilledCode);
    }

    [Theory]
    [InlineData(FreightTerms.PrepaidAndAdd, 12.345, 12.35)]
    [InlineData(FreightTerms.Prepaid, 12.345, 0)]
    [InlineData(FreightTerms.Collect, 12.345, 0)]
    [InlineData(FreightTerms.ThirdParty, 12.345, 0)]
    public void ShipmentFreightCharge_FollowsTheFreightTerms(FreightTerms terms, double cost, double expected)
        => FreightTermsResolver.DefaultCharge(terms, (decimal)cost).Should().Be((decimal)expected);

    [Fact]
    public void ShipmentFreightCharge_IsUnknownWithoutACarrierCost()
        => FreightTermsResolver.DefaultCharge(FreightTerms.PrepaidAndAdd, null).Should().BeNull();

    [Fact]
    public async Task InstallDefaultFreightTerms_ReadsTheSetting()
    {
        (await FreightTermsResolver.InstallDefaultAsync(_db, CancellationToken.None)).Should().Be(FreightTerms.PrepaidAndAdd);

        _db.SystemSettings.Add(new SystemSetting { Key = FreightTermsResolver.DefaultFreightTermsKey, Value = "Collect" });
        await _db.SaveChangesAsync();

        (await FreightTermsResolver.InstallDefaultAsync(_db, CancellationToken.None)).Should().Be(FreightTerms.Collect);
    }
}
