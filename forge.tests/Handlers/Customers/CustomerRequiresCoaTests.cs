using FluentAssertions;
using Moq;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

/// <summary>The customer's "requires certificate of analysis" flag is set and cleared on update, and an update that leaves it out keeps it.</summary>
public class CustomerRequiresCoaTests
{
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateCustomerHandler _handler;

    public CustomerRequiresCoaTests()
    {
        _handler = new UpdateCustomerHandler(
            _customerRepo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(), _db, Mock.Of<IClock>());
    }

    private Customer Seed(bool requiresCoa)
    {
        var customer = new Customer { Id = 5, Name = "Lab Supply", RequiresCoa = requiresCoa };
        _customerRepo.Setup(r => r.FindAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _customerRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return customer;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Sets_the_flag(bool before, bool after)
    {
        var customer = Seed(before);

        await _handler.Handle(new UpdateCustomerCommand(customer.Id, null, null, null, null, null, RequiresCoa: after), CancellationToken.None);

        customer.RequiresCoa.Should().Be(after);
    }

    [Fact]
    public async Task Leaving_it_out_keeps_it()
    {
        var customer = Seed(true);

        await _handler.Handle(new UpdateCustomerCommand(customer.Id, "Lab Supply Co", null, null, null, null), CancellationToken.None);

        customer.RequiresCoa.Should().BeTrue();
    }
}
