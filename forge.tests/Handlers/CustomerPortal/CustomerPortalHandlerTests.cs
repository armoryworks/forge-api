using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.CustomerPortal;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerPortal;

public class CustomerPortalHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IAccountingService> _accounting = new();
    private DateTimeOffset _now = Now;
    private int _tokenCounter;

    public CustomerPortalHandlerTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(() => _now);
        _accounting.Setup(a => a.ProviderId).Returns("mock");
    }

    private RequestMagicLinkHandler MagicLinkHandler()
    {
        var auth = new Mock<IPortalAuthService>();
        auth.Setup(a => a.GenerateMagicLinkToken()).Returns(() =>
        {
            _tokenCounter++;
            return ($"token-{_tokenCounter}", $"hash-{_tokenCounter}");
        });
        return new RequestMagicLinkHandler(
            _db, auth.Object, _email.Object, new ConfigurationBuilder().Build(), _clock.Object,
            NullLogger<RequestMagicLinkHandler>.Instance);
    }

    private Task<RequestMagicLinkResult> RequestLink(string email) =>
        MagicLinkHandler().Handle(new RequestMagicLinkCommand(email, "https://erp.example.test"), CancellationToken.None);

    private async Task<Contact> SeedContactAsync(string email, bool? access)
    {
        var customer = new Customer { Name = $"Portal Customer {Guid.NewGuid():N}" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var contact = new Contact { CustomerId = customer.Id, FirstName = "Pat", LastName = "Buyer", Email = email };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        if (access is { } enabled)
        {
            _db.CustomerPortalAccesses.Add(new CustomerPortalAccess
            {
                ContactId = contact.Id,
                CustomerId = customer.Id,
                IsEnabled = enabled,
            });
            await _db.SaveChangesAsync();
        }
        return contact;
    }

    [Fact]
    public async Task A_contact_without_provisioned_access_gets_no_link_and_no_access_row()
    {
        await SeedContactAsync("nobody@customer.test", access: null);

        var result = await RequestLink("nobody@customer.test");

        result.DevLink.Should().BeNull();
        (await _db.CustomerPortalAccesses.CountAsync()).Should().Be(0);
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_disabled_contact_gets_no_link()
    {
        await SeedContactAsync("disabled@customer.test", access: false);

        var result = await RequestLink("disabled@customer.test");

        result.DevLink.Should().BeNull();
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_shared_email_finds_the_provisioned_contact_even_when_another_contact_comes_first()
    {
        await SeedContactAsync("shared@customer.test", access: null);
        var provisioned = await SeedContactAsync("Shared@Customer.test", access: true);

        var result = await RequestLink("shared@customer.test");

        result.DevLink.Should().NotBeNull();
        var access = await _db.CustomerPortalAccesses.AsNoTracking().SingleAsync();
        access.ContactId.Should().Be(provisioned.Id);
        access.OneTimeTokenHash.Should().Be("hash-1");
        access.OneTimeTokenExpiresAt.Should().Be(Now.AddMinutes(15));
    }

    [Fact]
    public async Task A_second_request_inside_the_cooldown_sends_nothing_and_keeps_the_first_token()
    {
        await SeedContactAsync("cooldown@customer.test", access: true);

        (await RequestLink("cooldown@customer.test")).DevLink.Should().NotBeNull();
        _now = Now.AddSeconds(30);
        var second = await RequestLink("cooldown@customer.test");

        second.DevLink.Should().BeNull();
        (await _db.CustomerPortalAccesses.AsNoTracking().SingleAsync()).OneTimeTokenHash.Should().Be("hash-1");
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);

        _now = Now.AddSeconds(61);
        var third = await RequestLink("cooldown@customer.test");

        third.DevLink.Should().NotBeNull();
        (await _db.CustomerPortalAccesses.AsNoTracking().SingleAsync()).OneTimeTokenHash.Should().Be("hash-2");
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_used_link_does_not_hold_back_a_new_one()
    {
        await SeedContactAsync("burned@customer.test", access: true);
        await RequestLink("burned@customer.test");
        var access = await _db.CustomerPortalAccesses.SingleAsync();
        access.OneTimeTokenHash = null;
        access.OneTimeTokenExpiresAt = null;
        await _db.SaveChangesAsync();

        (await RequestLink("burned@customer.test")).DevLink.Should().NotBeNull();
    }

    [Theory]
    [InlineData("pat@customer.test", true)]
    [InlineData("pat\0@customer.test", false)]
    [InlineData("", false)]
    [InlineData("not-an-email", false)]
    public void The_request_validator_rejects_bad_emails(string email, bool valid)
    {
        var result = new RequestMagicLinkCommandValidator()
            .Validate(new RequestMagicLinkCommand(email, "https://erp.example.test"));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void The_request_validator_rejects_an_overlong_email()
    {
        var email = new string('a', 195) + "@x.test";

        new RequestMagicLinkCommandValidator()
            .Validate(new RequestMagicLinkCommand(email, "https://erp.example.test"))
            .IsValid.Should().BeFalse();
    }

    private async Task<(int CustomerId, int CurrencyId)> SeedCustomerWithBookAsync()
    {
        _db.Currencies.Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        _db.Currencies.Add(new Currency { Id = 2, Code = "CAD", Name = "Canadian Dollar", Symbol = "$" });
        _db.Books.Add(new Book { Code = "MAIN", Name = "Main", FunctionalCurrencyId = 2, ReportingTimeZone = "UTC" });
        var customer = new Customer { Name = "Portal Customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return (customer.Id, 2);
    }

    private Invoice NewInvoice(int customerId, string number, InvoiceStatus status, int currencyId, decimal amount) => new()
    {
        CustomerId = customerId,
        InvoiceNumber = number,
        Status = status,
        CurrencyId = currencyId,
        InvoiceDate = Now,
        Lines = [new InvoiceLine { Description = "Widget", Quantity = 1, UnitPrice = amount, LineNumber = 1 }],
    };

    [Fact]
    public async Task Portal_invoices_hide_drafts_zero_a_voided_balance_and_carry_the_invoice_currency()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        _db.Invoices.AddRange(
            NewInvoice(customerId, "INV-DRAFT", InvoiceStatus.Draft, 1, 10m),
            NewInvoice(customerId, "INV-SENT", InvoiceStatus.Sent, 1, 25m),
            NewInvoice(customerId, "INV-VOID", InvoiceStatus.Voided, 1, 40m));
        await _db.SaveChangesAsync();

        var rows = (await new GetPortalInvoicesHandler(_db, _accounting.Object)
            .Handle(new GetPortalInvoicesQuery(customerId), CancellationToken.None)).Items;

        rows.Select(r => r.InvoiceNumber).Should().BeEquivalentTo(["INV-SENT", "INV-VOID"]);
        rows.Single(r => r.InvoiceNumber == "INV-VOID").Balance.Should().Be(0m);
        rows.Single(r => r.InvoiceNumber == "INV-SENT").Balance.Should().Be(25m);
        rows.Should().OnlyContain(r => r.CurrencyCode == "USD");
    }

    [Fact]
    public async Task Portal_orders_are_labelled_with_the_functional_currency()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        _db.SalesOrders.Add(new SalesOrder { CustomerId = customerId, OrderNumber = "SO-1", Status = SalesOrderStatus.Confirmed });
        await _db.SaveChangesAsync();

        var rows = await new GetPortalSalesOrdersHandler(_db)
            .Handle(new GetPortalSalesOrdersQuery(customerId), CancellationToken.None);

        rows.Items.Should().ContainSingle().Which.CurrencyCode.Should().Be("CAD");
        rows.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Portal_orders_and_quotes_use_the_customer_default_currency_when_set()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        (await _db.Customers.SingleAsync(c => c.Id == customerId)).DefaultCurrency = "usd";
        _db.SalesOrders.Add(new SalesOrder { CustomerId = customerId, OrderNumber = "SO-1", Status = SalesOrderStatus.Confirmed });
        await _db.SaveChangesAsync();
        await SeedQuoteAsync(customerId, "Q-OPEN", QuoteStatus.Sent, null);

        var orders = await new GetPortalSalesOrdersHandler(_db)
            .Handle(new GetPortalSalesOrdersQuery(customerId), CancellationToken.None);
        var quotes = await new GetPortalQuotesHandler(_db, _clock.Object)
            .Handle(new GetPortalQuotesQuery(customerId), CancellationToken.None);

        orders.Items.Should().ContainSingle().Which.CurrencyCode.Should().Be("USD");
        quotes.Items.Should().ContainSingle().Which.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public async Task Portal_invoices_read_the_canonical_paid_amount_and_balance()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        var invoice = NewInvoice(customerId, "INV-PART", InvoiceStatus.PartiallyPaid, 1, 100m);
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        _db.PaymentApplications.Add(new PaymentApplication { InvoiceId = invoice.Id, PaymentId = 1, Amount = 30m });
        await _db.SaveChangesAsync();

        var row = (await new GetPortalInvoicesHandler(_db, _accounting.Object)
            .Handle(new GetPortalInvoicesQuery(customerId), CancellationToken.None)).Items.Single();

        row.AmountPaid.Should().Be(30m);
        row.Balance.Should().Be(70m);
    }

    [Fact]
    public async Task A_full_list_reports_the_total_beyond_the_cap()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        for (var n = 0; n < PortalReadRules.ListCap + 5; n++)
            _db.SalesOrders.Add(new SalesOrder { CustomerId = customerId, OrderNumber = $"SO-{n}", Status = SalesOrderStatus.Confirmed });
        await _db.SaveChangesAsync();

        var rows = await new GetPortalSalesOrdersHandler(_db)
            .Handle(new GetPortalSalesOrdersQuery(customerId), CancellationToken.None);

        rows.Items.Should().HaveCount(PortalReadRules.ListCap);
        rows.TotalCount.Should().Be(PortalReadRules.ListCap + 5);
    }

    private async Task<int> SeedQuoteAsync(int customerId, string number, QuoteStatus status, DateTimeOffset? expires)
    {
        var quote = new Quote { CustomerId = customerId, QuoteNumber = number, Status = status, ExpirationDate = expires };
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();
        return quote.Id;
    }

    private async Task UseShopTimeZoneAsync(string timeZone)
    {
        _db.Plants.Add(new Plant { Code = "P1", Name = "Plant", TimeZone = timeZone, IsActive = true, IsDefault = true });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Portal_quotes_hide_drafts_and_report_a_sent_quote_past_its_day_as_expired()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        await UseShopTimeZoneAsync("America/Denver");
        await SeedQuoteAsync(customerId, "Q-DRAFT", QuoteStatus.Draft, null);
        await SeedQuoteAsync(customerId, "Q-OPEN", QuoteStatus.Sent, null);
        await SeedQuoteAsync(customerId, "Q-TODAY", QuoteStatus.Sent, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
        await SeedQuoteAsync(customerId, "Q-PAST", QuoteStatus.Sent, new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));

        var rows = (await new GetPortalQuotesHandler(_db, _clock.Object)
            .Handle(new GetPortalQuotesQuery(customerId), CancellationToken.None)).Items;

        rows.Select(r => r.QuoteNumber).Should().BeEquivalentTo(["Q-OPEN", "Q-TODAY", "Q-PAST"]);
        rows.Single(r => r.QuoteNumber == "Q-OPEN").Status.Should().Be("Sent");
        rows.Single(r => r.QuoteNumber == "Q-TODAY").Status.Should().Be("Sent");
        rows.Single(r => r.QuoteNumber == "Q-PAST").Status.Should().Be("Expired");
        rows.Should().OnlyContain(r => r.CurrencyCode == "CAD");
    }

    [Fact]
    public async Task An_expired_quote_cannot_be_accepted_from_the_portal()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        await UseShopTimeZoneAsync("America/Denver");
        var id = await SeedQuoteAsync(customerId, "Q-PAST", QuoteStatus.Sent, new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));

        var act = () => new RespondToQuoteHandler(_db, _clock.Object)
            .Handle(new RespondToQuoteCommand(id, customerId, 7, Accepted: true), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Q-PAST*");
        (await _db.Quotes.AsNoTracking().SingleAsync(q => q.Id == id)).Status.Should().Be(QuoteStatus.Sent);
    }

    [Fact]
    public async Task An_expired_quote_can_still_be_declined_and_the_response_is_logged_on_the_quote()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        await UseShopTimeZoneAsync("America/Denver");
        var contact = new Contact { CustomerId = customerId, FirstName = "Pat", LastName = "Buyer" };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        var id = await SeedQuoteAsync(customerId, "Q-PAST", QuoteStatus.Sent, new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));

        await new RespondToQuoteHandler(_db, _clock.Object)
            .Handle(new RespondToQuoteCommand(id, customerId, contact.Id, Accepted: false), CancellationToken.None);

        (await _db.Quotes.AsNoTracking().SingleAsync(q => q.Id == id)).Status.Should().Be(QuoteStatus.Declined);
        var log = await _db.ActivityLogs.AsNoTracking()
            .SingleAsync(a => a.EntityType == "Quote" && a.EntityId == id && a.Action == "portal_declined");
        log.Description.Should().Contain("Q-PAST").And.Contain("Pat Buyer");
        log.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void The_exchange_validator_rejects_an_empty_or_overlong_token()
    {
        var validator = new ExchangeMagicLinkCommandValidator();

        validator.Validate(new ExchangeMagicLinkCommand("")).IsValid.Should().BeFalse();
        validator.Validate(new ExchangeMagicLinkCommand(new string('a', 201))).IsValid.Should().BeFalse();
        validator.Validate(new ExchangeMagicLinkCommand("abc123")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_quote_can_still_be_accepted_on_its_expiration_day_in_the_shop_time_zone()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        await UseShopTimeZoneAsync("America/Denver");
        var id = await SeedQuoteAsync(customerId, "Q-TODAY", QuoteStatus.Sent, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));

        await new RespondToQuoteHandler(_db, _clock.Object)
            .Handle(new RespondToQuoteCommand(id, customerId, 7, Accepted: true), CancellationToken.None);

        var quote = await _db.Quotes.AsNoTracking().SingleAsync(q => q.Id == id);
        quote.Status.Should().Be(QuoteStatus.Accepted);
        quote.AcceptedDate.Should().Be(Now);
    }

    [Fact]
    public async Task A_non_sent_quote_conflict_names_the_quote_number()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        var id = await SeedQuoteAsync(customerId, "Q-DONE", QuoteStatus.Accepted, null);

        var act = () => new RespondToQuoteHandler(_db, _clock.Object)
            .Handle(new RespondToQuoteCommand(id, customerId, 7, Accepted: false), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("Quote Q-DONE *");
    }

    [Fact]
    public async Task The_dashboard_open_quote_count_leaves_out_expired_quotes()
    {
        var (customerId, _) = await SeedCustomerWithBookAsync();
        await UseShopTimeZoneAsync("America/Denver");
        await SeedQuoteAsync(customerId, "Q-OPEN", QuoteStatus.Sent, null);
        await SeedQuoteAsync(customerId, "Q-TODAY", QuoteStatus.Sent, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
        await SeedQuoteAsync(customerId, "Q-PAST", QuoteStatus.Sent, new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));

        var summary = await new GetPortalDashboardHandler(_db, _accounting.Object, _clock.Object)
            .Handle(new GetPortalDashboardQuery(customerId), CancellationToken.None);

        summary.OpenQuoteCount.Should().Be(2);
    }
}
