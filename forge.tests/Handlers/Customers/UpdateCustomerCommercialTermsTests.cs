using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class UpdateCustomerCommercialTermsTests
{
    private static UpdateCustomerHandler CreateHandler(AppDbContext db) => new(
        new CustomerRepository(db), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
        db, Mock.Of<IClock>());

    private static async Task<Customer> SeedAsync(AppDbContext db, Action<Customer>? configure = null)
    {
        var customer = new Customer { Name = "Acme" };
        configure?.Invoke(customer);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    [Fact]
    public async Task Update_sets_credit_limit_and_tax_exemption_after_conversion()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db);

        await CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null,
                IsTaxExempt: true, TaxExemptionId: "UT-12345", CreditLimit: 25_000m),
            CancellationToken.None);

        var saved = await db.Customers.SingleAsync();
        saved.CreditLimit.Should().Be(25_000m);
        saved.IsTaxExempt.Should().BeTrue();
        saved.TaxExemptionId.Should().Be("UT-12345");
        (await db.ActivityLogs.SingleAsync(a => a.Action == "updated")).Description
            .Should().Contain("credit limit").And.Contain("tax exempt").And.Contain("tax exemption ID");
    }

    [Fact]
    public async Task Update_clears_the_credit_limit_on_request()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db, c => c.CreditLimit = 10_000m);

        await CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, ClearCreditLimit: true),
            CancellationToken.None);

        (await db.Customers.SingleAsync()).CreditLimit.Should().BeNull();
    }

    [Fact]
    public async Task Update_leaves_credit_and_tax_alone_when_not_supplied()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db, c =>
        {
            c.CreditLimit = 10_000m;
            c.IsTaxExempt = true;
            c.TaxExemptionId = "UT-1";
        });

        await CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, "Acme Renamed", null, null, null, null),
            CancellationToken.None);

        var saved = await db.Customers.SingleAsync();
        saved.CreditLimit.Should().Be(10_000m);
        saved.IsTaxExempt.Should().BeTrue();
        saved.TaxExemptionId.Should().Be("UT-1");
    }

    [Fact]
    public async Task Update_refuses_tax_exemption_without_an_exemption_id()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db);

        var act = () => CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, IsTaxExempt: true),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Validator_rejects_a_negative_credit_limit()
    {
        var result = new UpdateCustomerValidator().Validate(
            new UpdateCustomerCommand(1, null, null, null, null, null, CreditLimit: -1m));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Summary_returns_credit_limit_and_tax_exemption()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db, c =>
        {
            c.CreditLimit = 5_000m;
            c.IsTaxExempt = true;
            c.TaxExemptionId = "UT-9";
        });

        var summary = await new GetCustomerSummaryHandler(db, Mock.Of<IClock>())
            .Handle(new GetCustomerSummaryQuery(customer.Id), CancellationToken.None);

        summary.CreditLimit.Should().Be(5_000m);
        summary.IsTaxExempt.Should().BeTrue();
        summary.TaxExemptionId.Should().Be("UT-9");
    }

    [Fact]
    public async Task Update_sets_default_credit_terms_from_a_typed_spelling()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db);

        await CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, DefaultCreditTerms: "Net 45"),
            CancellationToken.None);

        (await db.Customers.SingleAsync()).DefaultCreditTerms.Should().Be(CreditTerms.Net45);
        (await db.ActivityLogs.SingleAsync(a => a.Action == "updated")).Description.Should().Contain("defaultCreditTerms");
    }

    [Fact]
    public async Task Update_clears_default_credit_terms_on_request_and_leaves_them_alone_otherwise()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db, c => c.DefaultCreditTerms = CreditTerms.Net60);

        await CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, "Acme Renamed", null, null, null, null), CancellationToken.None);
        (await db.Customers.SingleAsync()).DefaultCreditTerms.Should().Be(CreditTerms.Net60);

        await CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, ClearDefaultCreditTerms: true),
            CancellationToken.None);
        (await db.Customers.SingleAsync()).DefaultCreditTerms.Should().BeNull();
    }

    [Fact]
    public void Validator_rejects_unknown_default_credit_terms()
    {
        new UpdateCustomerValidator()
            .Validate(new UpdateCustomerCommand(1, null, null, null, null, null, DefaultCreditTerms: "Net 31"))
            .IsValid.Should().BeFalse();
    }
}
