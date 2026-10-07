using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class CustomerFaxTests
{
    [Fact]
    public async Task Update_stores_the_fax_and_names_it_in_the_activity_row()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var handler = new UpdateCustomerHandler(
            new CustomerRepository(db), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
            db, Mock.Of<IClock>());

        await handler.Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, Fax: "555-0100"),
            CancellationToken.None);

        (await db.Customers.SingleAsync()).Fax.Should().Be("555-0100");
        (await db.ActivityLogs.SingleAsync(a => a.Action == "updated")).Description.Should().Contain("fax");
    }

    [Fact]
    public async Task Detail_returns_the_customer_and_contact_fax_numbers()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme", Fax = "555-0100" };
        customer.Contacts.Add(new Contact { FirstName = "Ada", LastName = "Byron", Fax = "555-0101" });
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var result = await new GetCustomerByIdHandler(new CustomerRepository(db))
            .Handle(new GetCustomerByIdQuery(customer.Id), CancellationToken.None);

        result.Fax.Should().Be("555-0100");
        result.Contacts.Single().Fax.Should().Be("555-0101");
    }
}
