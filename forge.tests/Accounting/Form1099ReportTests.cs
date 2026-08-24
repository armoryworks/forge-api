using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// 1099 report aggregation over the InMemory provider: only <c>Is1099</c> vendors
/// are included, payments are filtered to the requested calendar year, the year
/// total is summed across a vendor's payments, and the $600 1099-NEC threshold flag
/// is set correctly (inclusive at exactly 600).
/// </summary>
public class Form1099ReportTests
{
    private static int _pmtSeq;

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Vendor>().AddRange(
            new Vendor { Id = 1, CompanyName = "Contractor A", Is1099 = true, TaxId = "12-3456789", IsActive = true },
            new Vendor { Id = 2, CompanyName = "Contractor B", Is1099 = true, TaxId = "98-7654321", IsActive = true },
            new Vendor { Id = 3, CompanyName = "Incorporated Supplier", Is1099 = false, IsActive = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task PayAsync(AppDbContext db, int vendorId, decimal amount, DateOnly date)
    {
        db.Set<VendorPayment>().Add(new VendorPayment
        {
            PaymentNumber = $"VPMT-{++_pmtSeq:D6}",
            VendorId = vendorId,
            Method = PaymentMethod.Check,
            Amount = amount,
            PaymentDate = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
    }

    private static Task<Form1099Report> ReportAsync(AppDbContext db, int year)
        => new Get1099ReportHandler(db).Handle(new Get1099ReportQuery(year), CancellationToken.None);

    [Fact]
    public async Task Report_IncludesOnly1099Vendors_AndSumsYearPayments()
    {
        using var db = await SeedAsync();
        await PayAsync(db, 1, 500m, new DateOnly(2025, 3, 1));
        await PayAsync(db, 1, 400m, new DateOnly(2025, 9, 1));
        await PayAsync(db, 3, 5000m, new DateOnly(2025, 6, 1)); // non-1099 vendor — excluded

        var report = await ReportAsync(db, 2025);

        report.Vendors.Should().OnlyContain(v => v.VendorId == 1 || v.VendorId == 2);
        var a = report.Vendors.Single(v => v.VendorId == 1);
        a.TotalPayments.Should().Be(900m);
        a.TaxId.Should().Be("12-3456789");
    }

    [Fact]
    public async Task Report_ExcludesPaymentsOutsideRequestedYear()
    {
        using var db = await SeedAsync();
        await PayAsync(db, 1, 700m, new DateOnly(2025, 12, 31));
        await PayAsync(db, 1, 999m, new DateOnly(2024, 12, 31)); // prior year
        await PayAsync(db, 1, 999m, new DateOnly(2026, 1, 1));   // next year

        var report = await ReportAsync(db, 2025);

        report.Vendors.Single(v => v.VendorId == 1).TotalPayments.Should().Be(700m);
    }

    [Fact]
    public async Task Report_ThresholdFlag_IsInclusiveAt600()
    {
        using var db = await SeedAsync();
        await PayAsync(db, 1, 600m, new DateOnly(2025, 4, 1)); // exactly at threshold
        await PayAsync(db, 2, 599.99m, new DateOnly(2025, 4, 1)); // just under

        var report = await ReportAsync(db, 2025);

        report.Vendors.Single(v => v.VendorId == 1).MeetsThreshold.Should().BeTrue();
        report.Vendors.Single(v => v.VendorId == 2).MeetsThreshold.Should().BeFalse();
        report.Threshold.Should().Be(600m);
        report.ReportableVendorCount.Should().Be(1);
    }

    [Fact]
    public async Task Report_FlaggedVendorWithNoPayments_IsListedAtZero()
    {
        using var db = await SeedAsync();
        await PayAsync(db, 1, 1000m, new DateOnly(2025, 4, 1));

        var report = await ReportAsync(db, 2025);

        var b = report.Vendors.Single(v => v.VendorId == 2);
        b.TotalPayments.Should().Be(0m);
        b.MeetsThreshold.Should().BeFalse();
        report.TotalPayments.Should().Be(1000m);
    }
}
