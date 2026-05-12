using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// Coverage for the install's base-currency reader. The service is just
/// "read currency.base from system_settings; default USD; cache 5 min" —
/// these tests pin all three behaviours.
/// </summary>
public class CurrencyServiceTests
{
    private static CurrencyService CreateService(out IMemoryCache cache, Data.Context.AppDbContext db)
    {
        cache = new MemoryCache(new MemoryCacheOptions());
        var repo = new SystemSettingRepository(db);
        return new CurrencyService(repo, cache, NullLogger<CurrencyService>.Instance);
    }

    [Fact]
    public async Task GetBaseCurrencyAsync_ReturnsUsd_WhenSettingMissing()
    {
        // Arrange — empty system_settings.
        using var db = TestDbContextFactory.Create();
        var service = CreateService(out _, db);

        // Act
        var result = await service.GetBaseCurrencyAsync(CancellationToken.None);

        // Assert
        result.Should().Be("USD");
    }

    [Fact]
    public async Task GetBaseCurrencyAsync_ReturnsConfiguredValue_WhenSettingPresent()
    {
        // Arrange — admin has configured currency.base = EUR.
        using var db = TestDbContextFactory.Create();
        db.SystemSettings.Add(new SystemSetting
        {
            Key = "currency.base",
            Value = "EUR",
            Description = "Install base currency",
        });
        await db.SaveChangesAsync();

        var service = CreateService(out _, db);

        // Act
        var result = await service.GetBaseCurrencyAsync(CancellationToken.None);

        // Assert
        result.Should().Be("EUR");
    }

    [Fact]
    public async Task GetBaseCurrencyAsync_CacheHit_ReturnsCachedValue()
    {
        // Arrange — first call populates the 5-minute cache, then we mutate the
        // underlying setting directly. Within the TTL window the cached value
        // must continue to win.
        using var db = TestDbContextFactory.Create();
        db.SystemSettings.Add(new SystemSetting
        {
            Key = "currency.base",
            Value = "GBP",
        });
        await db.SaveChangesAsync();

        var service = CreateService(out _, db);

        // Prime the cache.
        var first = await service.GetBaseCurrencyAsync(CancellationToken.None);
        first.Should().Be("GBP");

        // Mutate the setting under the service. A non-cached implementation
        // would now return CAD.
        var setting = db.SystemSettings.Single(s => s.Key == "currency.base");
        setting.Value = "CAD";
        await db.SaveChangesAsync();

        // Act — within TTL, should still see GBP from cache.
        var second = await service.GetBaseCurrencyAsync(CancellationToken.None);

        // Assert
        second.Should().Be("GBP");
    }

    [Fact]
    public async Task GetBaseCurrencyAsync_TreatsBlankValueAsUnset()
    {
        // Arrange — defensively handle a row that exists but has an empty Value.
        using var db = TestDbContextFactory.Create();
        db.SystemSettings.Add(new SystemSetting
        {
            Key = "currency.base",
            Value = "   ",
        });
        await db.SaveChangesAsync();

        var service = CreateService(out _, db);

        // Act
        var result = await service.GetBaseCurrencyAsync(CancellationToken.None);

        // Assert
        result.Should().Be("USD");
    }
}
