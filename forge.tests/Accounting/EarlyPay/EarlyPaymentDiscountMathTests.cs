using FluentAssertions;
using FluentValidation;
using System.Security.Claims;

using Forge.Api.Features.PaymentTerms;
using Forge.Core.Entities;
using Forge.Core.Models.Accounting;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting.EarlyPay;

public class EarlyPaymentDiscountMathTests
{
    private static readonly DateTimeOffset Jan25 = new(2026, 1, 25, 0, 0, 0, TimeSpan.FromHours(-7));

    [Fact]
    public void Dates_count_calendar_days_across_month_end()
    {
        EarlyPaymentDiscount.DueDate(Jan25, 30).Should().Be(new DateTimeOffset(2026, 2, 24, 0, 0, 0, TimeSpan.FromHours(-7)));
        EarlyPaymentDiscount.DiscountDueDate(Jan25, 2m, 10).Should().Be(new DateTimeOffset(2026, 2, 4, 0, 0, 0, TimeSpan.FromHours(-7)));
        EarlyPaymentDiscount.DiscountDueDate(Jan25, 0m, 10).Should().BeNull();
        EarlyPaymentDiscount.DueDate(Jan25, 0).Should().Be(Jan25);
        EarlyPaymentDiscount.MatchesDueDate(Jan25, Jan25.AddDays(30), 30).Should().BeTrue();
        EarlyPaymentDiscount.MatchesDueDate(Jan25, Jan25.AddDays(31), 30).Should().BeFalse();
    }

    [Fact]
    public void Window_is_inclusive_of_the_discount_date_and_extends_by_grace_days()
    {
        var discountDate = EarlyPaymentDiscount.DiscountDueDate(Jan25, 2m, 10);

        EarlyPaymentDiscount.InWindow(discountDate, Jan25.AddDays(8), 0).Should().BeTrue();
        EarlyPaymentDiscount.InWindow(discountDate, Jan25.AddDays(10).AddHours(20), 0).Should().BeTrue();
        EarlyPaymentDiscount.InWindow(discountDate, Jan25.AddDays(11), 0).Should().BeFalse();
        EarlyPaymentDiscount.InWindow(discountDate, Jan25.AddDays(12), 2).Should().BeTrue();
        EarlyPaymentDiscount.InWindow(null, Jan25, 5).Should().BeFalse();
    }

    [Fact]
    public void Automatic_discount_full_and_partial_payments()
    {
        EarlyPaymentDiscount.Maximum(9800m, 2m).Should().Be(196m);

        EarlyPaymentDiscount.Automatic(9604m, 9800m, 196m, 2m).Should().Be(196m);
        EarlyPaymentDiscount.Automatic(9800m, 9800m, 196m, 2m).Should().Be(0m);
        EarlyPaymentDiscount.Automatic(9700m, 9800m, 196m, 2m).Should().Be(100m);
        EarlyPaymentDiscount.Automatic(4900m, 9800m, 196m, 2m).Should().Be(100m);
        EarlyPaymentDiscount.Automatic(4900m, 4900m, 96m, 2m).Should().Be(0m);
        EarlyPaymentDiscount.Automatic(100m, 9800m, 0m, 2m).Should().Be(0m);
    }

    [Theory]
    [InlineData("Net 30", "Net30", "Net 30", true)]
    [InlineData("net30", "Net30", "Net 30", true)]
    [InlineData("2/10 Net 30", "2/10Net30", "2/10 Net 30", true)]
    [InlineData("2/10 net 60", "2/10Net30", "2/10 Net 30", false)]
    [InlineData("", "Net30", "Net 30", false)]
    public void Free_text_matches_terms_by_code_or_name(string text, string code, string name, bool expected)
        => PaymentTermsText.Matches(text, code, name).Should().Be(expected);

    private static Invoice DiscountInvoice(decimal total = 9800m)
        => new()
        {
            Id = 5, InvoiceNumber = "INV-1", InvoiceDate = Jan25, DueDate = Jan25.AddDays(30),
            DiscountPercent = 2m, DiscountDueDate = Jan25.AddDays(10),
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = total, LineNumber = 1 }],
        };

    private static ClaimsPrincipal User(string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

    [Fact]
    public void Balance_due_subtracts_discounts_taken()
    {
        var invoice = DiscountInvoice();
        invoice.PaymentApplications.Add(new PaymentApplication { Amount = 9604m, DiscountAmount = 196m });

        invoice.DiscountsTaken.Should().Be(196m);
        invoice.BalanceDue.Should().Be(0m);

        var bill = new VendorBill
        {
            Lines = [new VendorBillLine { Description = "Steel", Quantity = 1m, UnitPrice = 5000m, LineNumber = 1 }],
        };
        bill.PaymentApplications.Add(new VendorPaymentApplication { Amount = 4900m, DiscountAmount = 100m });
        bill.BalanceDue.Should().Be(0m);
    }

    [Fact]
    public void Explicit_discount_within_terms_is_taken_and_null_is_automatic()
    {
        using var db = TestDbContextFactory.Create();
        var invoice = DiscountInvoice();

        EarlyPaymentDiscountRules.ForInvoice(db, invoice, 9604m, Jan25.AddDays(8), null, false, null, DiscountSettings.Default, "d")
            .Should().Be(196m);
        EarlyPaymentDiscountRules.ForInvoice(db, invoice, 9604m, Jan25.AddDays(8), 150m, false, null, DiscountSettings.Default, "d")
            .Should().Be(150m);
        EarlyPaymentDiscountRules.ForInvoice(db, invoice, 9604m, Jan25.AddDays(12), null, false, null, DiscountSettings.Default, "d")
            .Should().Be(0m);
        EarlyPaymentDiscountRules.ForInvoice(db, invoice, 9604m, Jan25.AddDays(12), 0m, false, null, DiscountSettings.Default, "d")
            .Should().Be(0m);
    }

    [Fact]
    public void Discount_outside_the_window_or_above_the_maximum_is_refused()
    {
        using var db = TestDbContextFactory.Create();
        var invoice = DiscountInvoice();

        var late = () => EarlyPaymentDiscountRules.ForInvoice(
            db, invoice, 9604m, Jan25.AddDays(12), 196m, false, null, DiscountSettings.Default, "Applications[0].DiscountAmount");
        late.Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainSingle(e => e.ErrorCode == EarlyPaymentDiscountRules.NotAllowedCode
                && e.PropertyName == "Applications[0].DiscountAmount");

        var tooMuch = () => EarlyPaymentDiscountRules.ForInvoice(
            db, invoice, 9500m, Jan25.AddDays(8), 300m, false, null, DiscountSettings.Default, "d");
        tooMuch.Should().Throw<ValidationException>();

        var officeManager = () => EarlyPaymentDiscountRules.ForInvoice(
            db, invoice, 9500m, Jan25.AddDays(12), 300m, true, User("OfficeManager"), DiscountSettings.Default, "d");
        officeManager.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Admin_or_manager_override_is_taken_and_audited()
    {
        using var db = TestDbContextFactory.Create();
        var invoice = DiscountInvoice();

        EarlyPaymentDiscountRules.ForInvoice(db, invoice, 9500m, Jan25.AddDays(12), 300m, true, User("Manager"), DiscountSettings.Default, "d")
            .Should().Be(300m);

        db.ChangeTracker.Entries<AuditLogEntry>().Should().ContainSingle(e =>
            e.Entity.Action == EarlyPaymentDiscountRules.OverrideAuditAction && e.Entity.EntityType == "Invoice" && e.Entity.EntityId == 5);
    }

    [Fact]
    public void Grace_days_and_subtotal_base_come_from_settings()
    {
        using var db = TestDbContextFactory.Create();
        var invoice = DiscountInvoice();
        invoice.TaxRate = 0.1m;

        EarlyPaymentDiscountRules.RemainingMaximum(invoice, DiscountSettings.Default).Should().Be(215.60m);
        EarlyPaymentDiscountRules.RemainingMaximum(invoice, new DiscountSettings(true, 0)).Should().Be(196m);
        EarlyPaymentDiscountRules.AvailableOn(invoice, Jan25.AddDays(12), new DiscountSettings(false, 2)).Should().Be(215.60m);
        EarlyPaymentDiscountRules.AvailableOn(invoice, Jan25.AddDays(13), new DiscountSettings(false, 2)).Should().Be(0m);
    }

    [Fact]
    public async Task Settings_read_the_base_and_grace_days()
    {
        using var db = TestDbContextFactory.Create();
        db.SystemSettings.AddRange(
            new SystemSetting { Key = Forge.Core.Settings.PaymentTermsSettings.DiscountBaseKey, Value = "Subtotal" },
            new SystemSetting { Key = Forge.Core.Settings.PaymentTermsSettings.DiscountGraceDaysKey, Value = "3" });
        await db.SaveChangesAsync();

        var settings = await DiscountSettings.ReadAsync(db, CancellationToken.None);

        settings.Should().Be(new DiscountSettings(true, 3));
    }
}
