using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.SalesTax;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesTax;

public class GetTaxRateForCustomerHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static GetTaxRateForCustomerHandler Handler(AppDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return new GetTaxRateForCustomerHandler(db, clock.Object);
    }

    private static async Task<(SalesTaxRate state, SalesTaxRate county, SalesTaxRate exempt)> SeedUtahAsync(AppDbContext db)
    {
        var state = new SalesTaxRate { Name = "Utah state", Code = "UT-STATE", StateCode = "UT", Rate = 0.0485m, EffectiveFrom = Jan1 };
        var county = new SalesTaxRate { Name = "Utah County", Code = "UT-UTCO", StateCode = "UT", Rate = 0.0725m, EffectiveFrom = Jan1, IsDefault = true };
        var exempt = new SalesTaxRate { Name = "Resale exempt", Code = "EXEMPT-RESALE", StateCode = "UT", Rate = 0m, EffectiveFrom = Jan1, ExemptFlag = true };
        db.SalesTaxRates.AddRange(state, county, exempt);
        await db.SaveChangesAsync();
        return (state, county, exempt);
    }

    private static async Task<Customer> AddUtahCustomerAsync(AppDbContext db, Action<Customer>? configure = null)
    {
        var customer = new Customer { Name = "Utah buyer" };
        configure?.Invoke(customer);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.CustomerAddresses.Add(new CustomerAddress
        {
            CustomerId = customer.Id, Label = "HQ", Line1 = "1 Main", City = "Provo", State = "ut", PostalCode = "84601", IsDefault = true,
        });
        await db.SaveChangesAsync();
        return customer;
    }

    [Fact]
    public async Task Taxable_customer_never_gets_the_exemption_marker_and_ties_resolve_to_the_default_row()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUtahAsync(db);
        var customer = await AddUtahCustomerAsync(db);

        var result = await Handler(db).Handle(new GetTaxRateForCustomerQuery(customer.Id), CancellationToken.None);

        result!.Code.Should().Be("UT-UTCO");
        result.Rate.Should().Be(0.0725m);
    }

    [Fact]
    public async Task Customer_default_tax_code_wins_over_the_state_rate()
    {
        using var db = TestDbContextFactory.Create();
        var (state, _, _) = await SeedUtahAsync(db);
        var customer = await AddUtahCustomerAsync(db, c => c.DefaultTaxCodeId = state.Id);

        var result = await Handler(db).Handle(new GetTaxRateForCustomerQuery(customer.Id), CancellationToken.None);

        result!.Code.Should().Be("UT-STATE");
    }

    [Fact]
    public async Task Exempt_customer_gets_the_exemption_marker()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUtahAsync(db);
        var customer = await AddUtahCustomerAsync(db, c => c.IsTaxExempt = true);

        var result = await Handler(db).Handle(new GetTaxRateForCustomerQuery(customer.Id), CancellationToken.None);

        result!.Code.Should().Be("EXEMPT-RESALE");
        result.Rate.Should().Be(0m);
    }

    [Fact]
    public async Task Lapsed_exemption_skips_the_exempt_default_code_and_returns_the_jurisdiction_rate()
    {
        using var db = TestDbContextFactory.Create();
        var (_, _, exempt) = await SeedUtahAsync(db);
        var customer = await AddUtahCustomerAsync(db, c =>
        {
            c.IsTaxExempt = true;
            c.ExemptionExpiryDate = new DateOnly(2026, 10, 1);
            c.DefaultTaxCodeId = exempt.Id;
        });

        var result = await Handler(db).Handle(new GetTaxRateForCustomerQuery(customer.Id), CancellationToken.None);

        result!.Code.Should().Be("UT-UTCO");
    }

    [Fact]
    public async Task Ended_rate_is_not_returned()
    {
        using var db = TestDbContextFactory.Create();
        var (_, county, _) = await SeedUtahAsync(db);
        county.EffectiveTo = Jan1.AddDays(30);
        await db.SaveChangesAsync();
        var customer = await AddUtahCustomerAsync(db);

        var result = await Handler(db).Handle(new GetTaxRateForCustomerQuery(customer.Id), CancellationToken.None);

        result!.Code.Should().Be("UT-STATE");
    }

    [Fact]
    public async Task Exempt_customer_with_a_taxable_default_code_still_gets_the_exemption_marker()
    {
        using var db = TestDbContextFactory.Create();
        var (_, county, _) = await SeedUtahAsync(db);
        var customer = await AddUtahCustomerAsync(db, c =>
        {
            c.IsTaxExempt = true;
            c.DefaultTaxCodeId = county.Id;
        });

        var result = await Handler(db).Handle(new GetTaxRateForCustomerQuery(customer.Id), CancellationToken.None);

        result!.Code.Should().Be("EXEMPT-RESALE");
        result.Rate.Should().Be(0m);
    }

    [Fact]
    public async Task Exempt_customer_never_gets_a_taxable_rate_when_no_exempt_rate_exists()
    {
        using var db = TestDbContextFactory.Create();
        var (_, _, exempt) = await SeedUtahAsync(db);
        exempt.IsActive = false;
        await db.SaveChangesAsync();
        var customer = await AddUtahCustomerAsync(db, c => c.IsTaxExempt = true);

        var result = await Handler(db).Handle(new GetTaxRateForCustomerQuery(customer.Id), CancellationToken.None);

        result.Should().BeNull();
    }

    private static TaxOverrideGuard Guard(AppDbContext db)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetTaxRateForCustomerQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetTaxRateForCustomerQuery q, CancellationToken ct) => Handler(db).Handle(q, ct));
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return new TaxOverrideGuard(db, mediator.Object, clock.Object);
    }

    [Fact]
    public async Task Override_guard_lets_an_exempt_customer_be_quoted_at_zero_without_a_certificate()
    {
        using var db = TestDbContextFactory.Create();
        await SeedUtahAsync(db);
        var customer = await AddUtahCustomerAsync(db, c => c.IsTaxExempt = true);
        var guard = Guard(db);

        var baseline = await guard.GetDefaultRateAsync(customer.Id, CancellationToken.None);
        var documentId = await guard.EnsureCanOverrideAsync(customer.Id, 0m, baseline, CancellationToken.None);

        baseline.Should().Be(0m);
        documentId.Should().BeNull();
    }

    [Fact]
    public async Task Override_guard_needs_a_certificate_to_tax_an_exempt_customer()
    {
        using var db = TestDbContextFactory.Create();
        var (_, county, _) = await SeedUtahAsync(db);
        var customer = await AddUtahCustomerAsync(db, c =>
        {
            c.IsTaxExempt = true;
            c.DefaultTaxCodeId = county.Id;
        });
        var guard = Guard(db);

        var baseline = await guard.GetDefaultRateAsync(customer.Id, CancellationToken.None);
        var act = () => guard.EnsureCanOverrideAsync(customer.Id, county.Rate, baseline, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
