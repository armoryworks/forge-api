using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Data;
using Forge.Api.Features.Invoices;
using Forge.Api.Features.PaymentTerms;
using Forge.Api.Features.VendorBills;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting.EarlyPay;

public class PaymentTermsHandlerTests
{
    private static readonly DateTimeOffset InvoiceDate = new(2026, 1, 25, 0, 0, 0, TimeSpan.FromHours(-7));

    private static async Task<AppDbContext> SeededAsync()
    {
        var db = TestDbContextFactory.Create();
        await SeedData.EnsurePaymentTermsAsync(db);
        return db;
    }

    private static Task<PaymentTerm> TermsAsync(AppDbContext db, string code)
        => db.PaymentTerms.SingleAsync(t => t.Code == code);

    [Fact]
    public async Task Seed_adds_the_system_terms_once()
    {
        using var db = await SeededAsync();
        await SeedData.EnsurePaymentTermsAsync(db);

        (await db.PaymentTerms.CountAsync()).Should().Be(SeedData.SystemPaymentTerms.Length);
        var net30 = await TermsAsync(db, "Net30");
        net30.LegacyCreditTerms.Should().Be(CreditTerms.Net30);
        net30.IsSystem.Should().BeTrue();
        var twoTen = await TermsAsync(db, "2/10Net30");
        twoTen.DiscountPercent.Should().Be(2m);
        twoTen.DiscountDays.Should().Be(10);
        twoTen.NetDays.Should().Be(30);
    }

    [Fact]
    public async Task Create_refuses_a_discount_that_ends_after_the_net_date()
    {
        var result = new CreatePaymentTermValidator().Validate(
            new CreatePaymentTermCommand(new PaymentTermRequestModel("3/30Net30", "3/30 Net 30", 30, 3m, 30)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == PaymentTermValidator.DiscountDaysCode);

        using var db = await SeededAsync();
        var created = await new CreatePaymentTermHandler(db).Handle(
            new CreatePaymentTermCommand(new PaymentTermRequestModel("1/15Net45", "1/15 Net 45", 45, 1m, 15)), CancellationToken.None);
        created.IsSystem.Should().BeFalse();
        created.DiscountDays.Should().Be(15);

        var duplicate = () => new CreatePaymentTermHandler(db).Handle(
            new CreatePaymentTermCommand(new PaymentTermRequestModel("1/15Net45", "Again", 45, 0m, 0)), CancellationToken.None);
        (await duplicate.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == CreatePaymentTermHandler.DuplicateCode);
    }

    [Fact]
    public async Task System_terms_take_a_new_name_only()
    {
        using var db = await SeededAsync();
        var net30 = await TermsAsync(db, "Net30");
        var handler = new UpdatePaymentTermHandler(db);

        var renamed = await handler.Handle(new UpdatePaymentTermCommand(net30.Id,
            new PaymentTermRequestModel("Net30", "Thirty days", 30, 0m, 0, true, net30.SortOrder)), CancellationToken.None);
        renamed.Name.Should().Be("Thirty days");

        var changedDays = () => handler.Handle(new UpdatePaymentTermCommand(net30.Id,
            new PaymentTermRequestModel("Net30", "Thirty days", 31, 0m, 0, true, net30.SortOrder)), CancellationToken.None);
        (await changedDays.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == UpdatePaymentTermHandler.SystemTermsCode);

        var deactivated = () => handler.Handle(new UpdatePaymentTermCommand(net30.Id,
            new PaymentTermRequestModel("Net30", "Thirty days", 30, 0m, 0, false, net30.SortOrder)), CancellationToken.None);
        await deactivated.Should().ThrowAsync<ValidationException>();
    }

    private static (CreateInvoiceHandler Handler, Func<Invoice?> Saved) InvoiceHandler(AppDbContext db, Customer customer)
    {
        Invoice? saved = null;
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("INV-0300");
        invoices.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => saved = inv);
        var customers = new Mock<ICustomerRepository>();
        customers.Setup(r => r.FindAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        return (new CreateInvoiceHandler(invoices.Object, customers.Object, db), () => saved);
    }

    private static CreateInvoiceCommand InvoiceCommand(int customerId, int dueInDays, int? termsId = null, string? legacy = null)
        => new(customerId, null, null, InvoiceDate, InvoiceDate.AddDays(dueInDays), legacy, 0m, null,
            [new CreateInvoiceLineModel(null, "Widget", 1m, 9800m)], PaymentTermsId: termsId);

    private static async Task<Customer> AddCustomerAsync(AppDbContext db, int? termsId = null, CreditTerms? legacy = null)
    {
        var customer = new Customer { Name = "Acme Corp", PaymentTermsId = termsId, DefaultCreditTerms = legacy };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    [Fact]
    public async Task Picked_discount_terms_snapshot_the_discount_and_clear_the_legacy_value()
    {
        using var db = await SeededAsync();
        var twoTen = await TermsAsync(db, "2/10Net30");
        var customer = await AddCustomerAsync(db);
        var (handler, saved) = InvoiceHandler(db, customer);

        await handler.Handle(InvoiceCommand(customer.Id, 30, twoTen.Id), CancellationToken.None);

        saved()!.PaymentTermsId.Should().Be(twoTen.Id);
        saved()!.DiscountPercent.Should().Be(2m);
        saved()!.DiscountDueDate.Should().Be(InvoiceDate.AddDays(10));
        saved()!.CreditTerms.Should().BeNull();
    }

    [Fact]
    public async Task Picked_net_terms_write_back_the_legacy_credit_terms()
    {
        using var db = await SeededAsync();
        var net45 = await TermsAsync(db, "Net45");
        var customer = await AddCustomerAsync(db);
        var (handler, saved) = InvoiceHandler(db, customer);

        await handler.Handle(InvoiceCommand(customer.Id, 45, net45.Id), CancellationToken.None);

        saved()!.CreditTerms.Should().Be(CreditTerms.Net45);
        saved()!.DiscountDueDate.Should().BeNull();
    }

    [Fact]
    public async Task Picked_terms_must_agree_with_the_due_date()
    {
        using var db = await SeededAsync();
        var twoTen = await TermsAsync(db, "2/10Net30");
        var customer = await AddCustomerAsync(db);
        var (handler, _) = InvoiceHandler(db, customer);

        var act = () => handler.Handle(InvoiceCommand(customer.Id, 20, twoTen.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == PaymentTermsResolver.DueDateCode);
    }

    [Fact]
    public async Task Customer_discount_terms_are_inherited_and_legacy_only_customers_map_to_terms()
    {
        using var db = await SeededAsync();
        var twoTen = await TermsAsync(db, "2/10Net30");
        var withTerms = await AddCustomerAsync(db, twoTen.Id);
        var (handler, saved) = InvoiceHandler(db, withTerms);

        await handler.Handle(InvoiceCommand(withTerms.Id, 30), CancellationToken.None);
        saved()!.PaymentTermsId.Should().Be(twoTen.Id);
        saved()!.DiscountPercent.Should().Be(2m);

        var legacyOnly = await AddCustomerAsync(db, legacy: CreditTerms.Net60);
        var (legacyHandler, legacySaved) = InvoiceHandler(db, legacyOnly);
        await legacyHandler.Handle(InvoiceCommand(legacyOnly.Id, 60), CancellationToken.None);
        legacySaved()!.PaymentTermsId.Should().Be((await TermsAsync(db, "Net60")).Id);
        legacySaved()!.CreditTerms.Should().Be(CreditTerms.Net60);
    }

    [Fact]
    public async Task Vendor_terms_follow_a_pick_and_match_typed_text()
    {
        using var db = await SeededAsync();
        var twoTen = await TermsAsync(db, "2/10Net30");
        var vendor = new Vendor { CompanyName = "Steel Supply" };

        (await VendorTerms.ApplyAsync(db, vendor, null, "2/10 net 30", CancellationToken.None)).Should().BeTrue();
        vendor.PaymentTermsId.Should().Be(twoTen.Id);
        vendor.PaymentTerms.Should().Be("2/10 net 30");

        (await VendorTerms.ApplyAsync(db, vendor, null, "Pay on the 15th", CancellationToken.None)).Should().BeTrue();
        vendor.PaymentTermsId.Should().BeNull();
        vendor.PaymentTerms.Should().Be("Pay on the 15th");

        var net15 = await TermsAsync(db, "Net15");
        (await VendorTerms.ApplyAsync(db, vendor, net15.Id, null, CancellationToken.None)).Should().BeTrue();
        vendor.PaymentTermsId.Should().Be(net15.Id);
        vendor.PaymentTerms.Should().Be("Net 15");
        (await VendorTerms.ApplyAsync(db, vendor, net15.Id, null, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task A_bill_inherits_its_vendors_discount_terms()
    {
        using var db = await SeededAsync();
        var twoTen = await TermsAsync(db, "2/10Net30");
        var vendor = new Vendor { CompanyName = "Steel Supply", PaymentTermsId = twoTen.Id };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var handler = new CreateVendorBillHandler(new VendorBillRepository(db), new VendorRepository(db), db);
        var result = await handler.Handle(new CreateVendorBillCommand(
            vendor.Id, "V-1", null, InvoiceDate, InvoiceDate.AddDays(30), 0m, null,
            [new CreateVendorBillLineModel(null, "Steel", 1m, 5000m, null)]), CancellationToken.None);

        var bill = await db.VendorBills.SingleAsync(b => b.Id == result.Id);
        bill.PaymentTermsId.Should().Be(twoTen.Id);
        bill.DiscountPercent.Should().Be(2m);
        bill.DiscountDueDate.Should().Be(InvoiceDate.AddDays(10));
        result.DiscountPercent.Should().Be(2m);
    }
}
