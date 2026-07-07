using FluentAssertions;
using Moq;

using Forge.Api.Features.CustomerAddresses;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerAddresses;

public class SetCustomerAddressActiveHandlerTests
{
    private readonly Mock<ICustomerAddressRepository> _repo = new();

    private SetCustomerAddressActiveHandler Handler(Forge.Data.Context.AppDbContext db)
        => new(_repo.Object, db);

    private static CustomerAddress Address(bool isDefault = false, bool isActive = true) => new()
    {
        Id = 5, CustomerId = 2, Label = "HQ", Line1 = "1 Main St",
        City = "Springfield", State = "IL", PostalCode = "62701",
        IsDefault = isDefault, IsActive = isActive,
    };

    [Fact]
    public async Task Deactivates_a_non_default_address_and_logs_activity()
    {
        using var db = TestDbContextFactory.Create();
        var address = Address();
        _repo.Setup(r => r.FindAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(address);

        await Handler(db).Handle(new SetCustomerAddressActiveCommand(5, false), CancellationToken.None);

        address.IsActive.Should().BeFalse();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_deactivating_the_default_address()
    {
        using var db = TestDbContextFactory.Create();
        _repo.Setup(r => r.FindAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Address(isDefault: true));

        var act = () => Handler(db).Handle(new SetCustomerAddressActiveCommand(5, false), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*default*");
    }

    [Fact]
    public async Task Reactivation_is_allowed_and_noop_when_already_active()
    {
        using var db = TestDbContextFactory.Create();
        var address = Address(isActive: true);
        _repo.Setup(r => r.FindAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(address);

        await Handler(db).Handle(new SetCustomerAddressActiveCommand(5, true), CancellationToken.None);

        address.IsActive.Should().BeTrue();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no-op toggles must not write");
    }
}
