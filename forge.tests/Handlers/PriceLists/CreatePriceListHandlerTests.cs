using FluentAssertions;
using Moq;

using Forge.Api.Features.PriceLists;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.PriceLists;

public class CreatePriceListHandlerTests
{
    private readonly Mock<IPriceListRepository> _repo = new();

    [Fact]
    public async Task Handle_ValidCommand_CreatesPriceListWithEntries()
    {
        // Arrange
        PriceList? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<PriceList>(), It.IsAny<CancellationToken>()))
            .Callback<PriceList, CancellationToken>((pl, _) => captured = pl);

        var command = new CreatePriceListCommand(
            "Standard Pricing", "Default price list", null, true, true, null, null,
            [
                new CreatePriceListEntryModel(1, 10.50m, 1),
                new CreatePriceListEntryModel(2, 9.00m, 100),
            ]);

        var handler = new CreatePriceListHandler(_repo.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Standard Pricing");
        result.IsDefault.Should().BeTrue();
        result.EntryCount.Should().Be(2);

        captured.Should().NotBeNull();
        captured!.Entries.Should().HaveCount(2);
        captured.Entries.First().UnitPrice.Should().Be(10.50m);
    }

    [Fact]
    public async Task Handle_WithCustomerAndDates_SetsFieldsCorrectly()
    {
        // Arrange
        PriceList? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<PriceList>(), It.IsAny<CancellationToken>()))
            .Callback<PriceList, CancellationToken>((pl, _) => captured = pl);

        var from = DateTimeOffset.UtcNow;
        var to = from.AddMonths(6);

        var command = new CreatePriceListCommand(
            "Acme Special", null, 42, false, true, from, to,
            [new CreatePriceListEntryModel(1, 8.00m, 500)]);

        var handler = new CreatePriceListHandler(_repo.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.CustomerId.Should().Be(42);
        captured.IsDefault.Should().BeFalse();
        captured.EffectiveFrom.Should().Be(from);
        captured.EffectiveTo.Should().Be(to);
    }

    [Fact]
    public async Task Handle_EntriesHaveCorrectMinQuantity()
    {
        // Arrange
        PriceList? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<PriceList>(), It.IsAny<CancellationToken>()))
            .Callback<PriceList, CancellationToken>((pl, _) => captured = pl);

        var command = new CreatePriceListCommand(
            "Quantity Breaks", null, null, false, true, null, null,
            [
                new CreatePriceListEntryModel(5, 20.00m, 1),
                new CreatePriceListEntryModel(5, 18.00m, 50),
                new CreatePriceListEntryModel(5, 15.00m, 200),
            ]);

        var handler = new CreatePriceListHandler(_repo.Object);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        captured!.Entries.Should().HaveCount(3);
        captured.Entries.Select(e => e.MinQuantity).Should().BeEquivalentTo([1, 50, 200]);
        captured.Entries.Select(e => e.UnitPrice).Should().BeEquivalentTo([20.00m, 18.00m, 15.00m]);
    }
}
