using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using QuestPDF.Infrastructure;

using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

/// <summary>
/// Emailing an invoice: the default recipient (Billing contact, then the customer's email, then the primary
/// contact), the finalized-only guard, and the amount in the invoice currency.
/// </summary>
public class InvoiceEmailDeliveryTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IIntegrationOutboxService> _outbox = new();

    public InvoiceEmailDeliveryTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _outbox.Setup(o => o.EnqueueEmailAsync(
                It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationOutboxEntry());
    }

    public void Dispose() => _db.Dispose();

    private async Task<Invoice> SeedAsync(
        string? customerEmail = "office@buyer.test", InvoiceStatus status = InvoiceStatus.Sent, bool withContacts = true)
    {
        _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Harbor Gear Works" });
        _db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        _db.ReferenceData.Add(new ReferenceData { GroupCode = "contact_role", Code = "billing", Label = "Accounts Payable" });
        var customer = new Customer { Name = "Buyer Co", Email = customerEmail };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        if (withContacts)
        {
            _db.Contacts.AddRange(
                new Contact { CustomerId = customer.Id, FirstName = "Pat", LastName = "Primary", Email = "pat@buyer.test", IsPrimary = true },
                new Contact { CustomerId = customer.Id, FirstName = "Elias", LastName = "Ledger", Email = "elias@buyer.test", Role = "Accounts Payable" });
        }

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-5100", CustomerId = customer.Id, CurrencyId = 1, Status = status,
            InvoiceDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero),
            Lines = { new InvoiceLine { LineNumber = 1, Description = "Bracket", Quantity = 4, UnitPrice = 300m } },
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    private SendInvoiceEmailHandler Handler()
        => new(_db, new SystemSettingRepository(_db), _outbox.Object);

    [Fact]
    public async Task Recipient_PrefersTheBillingContact_ByTheRoleLabel()
    {
        var invoice = await SeedAsync();

        var result = await new GetInvoiceEmailRecipientHandler(_db)
            .Handle(new GetInvoiceEmailRecipientQuery(invoice.Id), CancellationToken.None);

        result.Should().Be(new InvoiceEmailRecipientResponseModel(
            "elias@buyer.test", InvoiceRecipientResolver.SourceBillingContact, "Ledger, Elias"));
    }

    [Fact]
    public async Task Recipient_FallsBackToTheCustomerEmail_ThenThePrimaryContact()
    {
        var invoice = await SeedAsync(withContacts: false);
        (await InvoiceRecipientResolver.ResolveAsync(_db, invoice.CustomerId, CancellationToken.None))
            .Should().Be(new InvoiceEmailRecipientResponseModel("office@buyer.test", InvoiceRecipientResolver.SourceCustomer, null));

        var customer = await _db.Customers.SingleAsync();
        customer.Email = null;
        _db.Contacts.Add(new Contact { CustomerId = customer.Id, FirstName = "Pat", LastName = "Primary", Email = "pat@buyer.test", IsPrimary = true });
        await _db.SaveChangesAsync();

        (await InvoiceRecipientResolver.ResolveAsync(_db, invoice.CustomerId, CancellationToken.None)).Email
            .Should().Be("pat@buyer.test");
    }

    [Fact]
    public async Task Email_WithoutRecipient_GoesToTheBillingContact_AndLogsActivity()
    {
        var invoice = await SeedAsync();

        await Handler().Handle(new SendInvoiceEmailCommand(invoice.Id, null), CancellationToken.None);

        _outbox.Verify(o => o.EnqueueEmailAsync(
            It.IsAny<string>(),
            It.Is<EmailMessage>(m => m.To == "elias@buyer.test" && m.HtmlBody.Contains("$1,200.00") && !m.HtmlBody.Contains("¤")),
            "Invoice", invoice.Id, It.IsAny<CancellationToken>()), Times.Once);
        (await _db.ActivityLogs.AnyAsync(a => a.EntityType == "Invoice" && a.EntityId == invoice.Id && a.Action == "emailed"))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Voided)]
    public async Task Email_DraftOrVoided_IsRefused(InvoiceStatus status)
    {
        var invoice = await SeedAsync(status: status);

        var act = () => Handler().Handle(new SendInvoiceEmailCommand(invoice.Id, "elias@buyer.test"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _outbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Email_NoAddressAnywhere_IsRefused()
    {
        var invoice = await SeedAsync(customerEmail: null, withContacts: false);

        var act = () => Handler().Handle(new SendInvoiceEmailCommand(invoice.Id, " "), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no email address on file*");
    }
}
