using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Customers.BulkIntake;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class CustomerNumberingTests
{
    [Fact]
    public async Task Bulk_intake_gives_each_new_customer_its_own_number_after_the_highest()
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(new Customer { Name = "Existing", CustomerNumber = "CUST-00041" });
        db.Customers.Add(new Customer { Name = "Manual", CustomerNumber = "ACME-1" });
        await db.SaveChangesAsync();
        var identifiers = new Mock<IBusinessIdentifierService>();

        var request = new BulkCustomerIntakeRequest(
        [
            new BulkCustomerIntakeRow("r1", "Alpha", null, null, null, null),
            new BulkCustomerIntakeRow("r2", "Beta", null, null, null, null),
            new BulkCustomerIntakeRow("r3", "Gamma", null, null, null, null),
        ]);
        await new BulkCustomerIntakeHandler(db, identifiers.Object)
            .Handle(new BulkCustomerIntakeCommand(request, Commit: true), CancellationToken.None);

        var numbers = await db.Customers
            .Where(c => c.Name == "Alpha" || c.Name == "Beta" || c.Name == "Gamma")
            .OrderBy(c => c.Name)
            .Select(c => c.CustomerNumber)
            .ToListAsync();
        numbers.Should().Equal("CUST-00042", "CUST-00043", "CUST-00044");
        identifiers.Verify(i => i.IssueAsync(BusinessEntityType.Customer, It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Bulk_intake_preview_assigns_no_numbers()
    {
        using var db = TestDbContextFactory.Create();
        var identifiers = new Mock<IBusinessIdentifierService>();

        await new BulkCustomerIntakeHandler(db, identifiers.Object).Handle(
            new BulkCustomerIntakeCommand(new BulkCustomerIntakeRequest([new BulkCustomerIntakeRow("r1", "Alpha", null, null, null, null)]), Commit: false),
            CancellationToken.None);

        (await db.Customers.CountAsync()).Should().Be(0);
        identifiers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Customer_list_search_matches_the_customer_number()
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(new Customer { Name = "Alpha", CustomerNumber = "CUST-00007" });
        db.Customers.Add(new Customer { Name = "Beta", CustomerNumber = "CUST-00008" });
        await db.SaveChangesAsync();

        var page = await new CustomerRepository(db)
            .GetPagedAsync(new CustomerListQuery { Q = "cust-00008" }, CancellationToken.None);

        page.Items.Select(c => c.Name).Should().Equal("Beta");
    }
}
