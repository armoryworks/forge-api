using FluentAssertions;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

using Forge.Api.Features.Customers;
using Forge.Api.Features.Invoices;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class InvoiceCompanyNameTests : IDisposable
{
    private const string CompanyName = "Harbor Gear Works";
    private const string LegacyBrand = "QB Engineer";

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly SystemSettingRepository _settings;
    private readonly Mock<IIntegrationOutboxService> _outbox = new();

    public InvoiceCompanyNameTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _settings = new SystemSettingRepository(_db);
        _outbox.Setup(o => o.EnqueueEmailAsync(
                It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationOutboxEntry());
    }

    public void Dispose() => _db.Dispose();

    private async Task SetCompanyNameAsync(string value)
    {
        _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = value });
        await _db.SaveChangesAsync();
    }

    private async Task<Invoice> SeedInvoiceAsync()
    {
        var customer = new Customer { Name = "Buyer Co", Email = "ap@buyer.test" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-4100",
            CustomerId = customer.Id,
            Status = InvoiceStatus.Sent,
            InvoiceDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero),
            Lines = { new InvoiceLine { LineNumber = 1, Description = "Bracket", Quantity = 4, UnitPrice = 12.5m } },
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    private static string Svg(IDocument document) => string.Join("\n", document.GenerateSvg());

    [Fact]
    public async Task InvoicePdf_HeaderShowsCompanyName_WhenSet()
    {
        await SetCompanyNameAsync(CompanyName);
        var invoice = await SeedInvoiceAsync();

        var pdf = await new GenerateInvoicePdfHandler(_db, _settings)
            .Handle(new GenerateInvoicePdfQuery(invoice.Id), CancellationToken.None);
        pdf.Should().NotBeEmpty();

        var resolved = await CompanyIdentity.GetCompanyNameAsync(_settings, CancellationToken.None);
        var svg = Svg(new InvoicePdfDocument(invoice, resolved));

        svg.Should().Contain(CompanyName);
        svg.Should().NotContain(LegacyBrand);
    }

    [Fact]
    public async Task InvoicePdf_RendersWithoutCompanyHeader_WhenUnset()
    {
        var invoice = await SeedInvoiceAsync();

        var pdf = await new GenerateInvoicePdfHandler(_db, _settings)
            .Handle(new GenerateInvoicePdfQuery(invoice.Id), CancellationToken.None);
        pdf.Should().NotBeEmpty();

        var svg = Svg(new InvoicePdfDocument(invoice, null));

        svg.Should().Contain("INVOICE");
        svg.Should().NotContain(LegacyBrand);
    }

    [Fact]
    public void StatementPdf_HeaderShowsCompanyName_OrNothing()
    {
        var customer = new Customer { Name = "Buyer Co" };
        var statementDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var named = Svg(new CustomerStatementPdfDocument(customer, [], [], CompanyName, statementDate));
        var unnamed = Svg(new CustomerStatementPdfDocument(customer, [], [], null, statementDate));

        named.Should().Contain(CompanyName);
        unnamed.Should().Contain("CUSTOMER STATEMENT");
        unnamed.Should().NotContain(LegacyBrand);
    }

    [Fact]
    public async Task SendInvoiceEmail_WithoutCompanyName_ThrowsAndEnqueuesNothing()
    {
        await SetCompanyNameAsync("  ");
        var invoice = await SeedInvoiceAsync();
        var handler = new SendInvoiceEmailHandler(_db, _settings, _outbox.Object);

        var act = () => handler.Handle(
            new SendInvoiceEmailCommand(invoice.Id, "ap@buyer.test"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Set your company name in Admin > Company before sending documents to customers.");
        _outbox.Verify(o => o.EnqueueEmailAsync(
            It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendInvoiceEmail_WithCompanyName_UsesItInSubjectAndBody()
    {
        await SetCompanyNameAsync(CompanyName);
        var invoice = await SeedInvoiceAsync();
        var handler = new SendInvoiceEmailHandler(_db, _settings, _outbox.Object);

        await handler.Handle(
            new SendInvoiceEmailCommand(invoice.Id, "ap@buyer.test"), CancellationToken.None);

        _outbox.Verify(o => o.EnqueueEmailAsync(
            It.IsAny<string>(),
            It.Is<EmailMessage>(m =>
                m.Subject == $"Invoice INV-4100 from {CompanyName}"
                && m.HtmlBody.Contains(CompanyName)
                && !m.HtmlBody.Contains(LegacyBrand)),
            "Invoice", invoice.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
