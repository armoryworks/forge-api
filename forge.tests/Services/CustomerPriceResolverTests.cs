using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class CustomerPriceResolverTests
{
    private readonly Data.Context.AppDbContext _db = TestDbContextFactory.Create();
    private readonly CustomerPriceResolver _resolver;
    private Customer _customer = null!;
    private Part _part = null!;

    public CustomerPriceResolverTests()
    {
        _resolver = new CustomerPriceResolver(
            _db, new SystemClock(), new MockCurrencyService(NullLogger<MockCurrencyService>.Instance));
    }

    private async Task SeedAsync()
    {
        _customer = new Customer { Name = "Brindle", IsActive = true };
        _part = new Part
        {
            PartNumber = "SA-210", Name = "Manifold body", Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Make, InventoryClass = InventoryClass.FinishedGood,
        };
        _db.Customers.Add(_customer);
        _db.Parts.Add(_part);
        await _db.SaveChangesAsync();
    }

    private async Task<PriceList> AddListAsync(DateTimeOffset? effectiveFrom, params (int MinQty, decimal Price, string Currency)[] entries)
    {
        var list = new PriceList { Name = $"List {Guid.NewGuid():N}", CustomerId = _customer.Id, IsActive = true, EffectiveFrom = effectiveFrom };
        foreach (var (minQty, price, currency) in entries)
            list.Entries.Add(new PriceListEntry { PartId = _part.Id, MinQuantity = minQty, UnitPrice = price, Currency = currency });
        _db.PriceLists.Add(list);
        await _db.SaveChangesAsync();
        return list;
    }

    [Theory]
    [InlineData(250, 96)]
    [InlineData(100, 96)]
    [InlineData(99, 112)]
    [InlineData(1, 112)]
    public async Task Picks_the_highest_break_at_or_below_the_quantity(int quantity, decimal expected)
    {
        await SeedAsync();
        await AddListAsync(DateTimeOffset.UtcNow.AddDays(-30), (100, 96m, "USD"), (1, 112m, "USD"));

        (await _resolver.ResolveUnitPriceAsync(_customer.Id, _part.Id, quantity, CancellationToken.None))
            .Should().Be(expected);
    }

    [Fact]
    public async Task Uses_the_lowest_break_when_the_quantity_is_unknown()
    {
        await SeedAsync();
        await AddListAsync(DateTimeOffset.UtcNow.AddDays(-30), (100, 96m, "USD"), (1, 112m, "USD"));

        (await _resolver.ResolveUnitPriceAsync(_customer.Id, _part.Id, null, CancellationToken.None))
            .Should().Be(112m);
    }

    [Fact]
    public async Task A_dated_list_beats_an_open_ended_one()
    {
        await SeedAsync();
        await AddListAsync(null, (1, 140m, "USD"));
        await AddListAsync(DateTimeOffset.UtcNow.AddDays(-5), (1, 112m, "USD"));

        (await _resolver.ResolveUnitPriceAsync(_customer.Id, _part.Id, 10, CancellationToken.None))
            .Should().Be(112m);
    }

    [Fact]
    public async Task Skips_entries_priced_in_another_currency()
    {
        await SeedAsync();
        await AddListAsync(DateTimeOffset.UtcNow.AddDays(-5), (1, 96m, "CAD"));

        (await _resolver.ResolveUnitPriceAsync(_customer.Id, _part.Id, 10, CancellationToken.None))
            .Should().BeNull();
    }
}
