using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class CustomerActivityWordingTests
{
    private static UpdateCustomerHandler Handler(Forge.Data.Context.AppDbContext db) => new(
        new CustomerRepository(db), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
        db, Mock.Of<IClock>());

    [Fact]
    public async Task Update_names_fields_in_plain_words()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        await Handler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, IsFdaRegulated: true, CreditLimit: 5000m),
            CancellationToken.None);

        (await db.ActivityLogs.SingleAsync(a => a.Action == "updated")).Description
            .Should().Be("Updated 2 fields: credit limit, FDA regulated: yes");
    }

    [Fact]
    public async Task Summary_reports_the_credit_hold()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme", IsOnCreditHold = true };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var result = await new GetCustomerSummaryHandler(db, Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.UtcNow))
            .Handle(new GetCustomerSummaryQuery(customer.Id), CancellationToken.None);

        result.IsOnCreditHold.Should().BeTrue();
    }
}
