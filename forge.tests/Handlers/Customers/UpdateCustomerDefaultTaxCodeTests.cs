using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Controllers;
using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class UpdateCustomerDefaultTaxCodeTests
{
    private static UpdateCustomerHandler CreateHandler(AppDbContext db) => new(
        new CustomerRepository(db), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
        db, Mock.Of<IClock>());

    private static async Task<(Customer Customer, SalesTaxRate Active, SalesTaxRate Retired)> SeedAsync(AppDbContext db)
    {
        var active = new SalesTaxRate { Name = "Utah", Code = "UT", Rate = 0.0725m, IsActive = true };
        var retired = new SalesTaxRate { Name = "Old", Code = "OLD", Rate = 0.05m, IsActive = false };
        var customer = new Customer { Name = "Tax Co" };
        db.AddRange(active, retired, customer);
        await db.SaveChangesAsync();
        return (customer, active, retired);
    }

    [Fact]
    public async Task Sets_the_default_tax_code_after_create()
    {
        using var db = TestDbContextFactory.Create();
        var (customer, active, _) = await SeedAsync(db);

        await CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, DefaultTaxCodeId: active.Id),
            CancellationToken.None);

        (await db.Customers.SingleAsync()).DefaultTaxCodeId.Should().Be(active.Id);
        (await db.ActivityLogs.SingleAsync(a => a.Action == "updated")).Description.Should().Contain("default tax code");
    }

    [Fact]
    public async Task Clears_the_default_tax_code_on_request()
    {
        using var db = TestDbContextFactory.Create();
        var (customer, active, _) = await SeedAsync(db);
        customer.DefaultTaxCodeId = active.Id;
        await db.SaveChangesAsync();

        await CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, ClearDefaultTaxCode: true),
            CancellationToken.None);

        (await db.Customers.SingleAsync()).DefaultTaxCodeId.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_an_inactive_tax_code()
    {
        using var db = TestDbContextFactory.Create();
        var (customer, _, retired) = await SeedAsync(db);

        var act = () => CreateHandler(db).Handle(
            new UpdateCustomerCommand(customer.Id, null, null, null, null, null, DefaultTaxCodeId: retired.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Customers.SingleAsync()).DefaultTaxCodeId.Should().BeNull();
    }

    [Fact]
    public void Validator_refuses_a_code_and_a_clear_together()
    {
        var result = new UpdateCustomerValidator().Validate(
            new UpdateCustomerCommand(1, null, null, null, null, null, DefaultTaxCodeId: 3, ClearDefaultTaxCode: true));

        result.IsValid.Should().BeFalse();
    }

    private static CustomersController ControllerFor(Mock<IMediator> mediator, string role) => new(mediator.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role)], "Test")),
            },
        },
    };

    [Theory]
    [InlineData("PM")]
    [InlineData("Engineer")]
    public async Task Roles_without_commercial_access_cannot_change_the_tax_code(string role)
    {
        var mediator = new Mock<IMediator>();

        var result = await ControllerFor(mediator, role).UpdateCustomer(1,
            new UpdateCustomerRequestModel(null, null, null, null, null, DefaultTaxCodeId: 4));

        result.Should().BeOfType<ForbidResult>();
        mediator.Verify(m => m.Send(It.IsAny<UpdateCustomerCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("OfficeManager")]
    public async Task Commercial_roles_change_the_tax_code(string role)
    {
        var mediator = new Mock<IMediator>();

        var result = await ControllerFor(mediator, role).UpdateCustomer(1,
            new UpdateCustomerRequestModel(null, null, null, null, null, DefaultTaxCodeId: 4));

        result.Should().BeOfType<NoContentResult>();
        mediator.Verify(m => m.Send(It.Is<UpdateCustomerCommand>(c => c.DefaultTaxCodeId == 4), It.IsAny<CancellationToken>()), Times.Once);
    }
}
