using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;

namespace Forge.Tests.Accounting;

/// <summary>
/// §5.5 — the <see cref="ForgeGlAccountingService"/> provider shim. It must be a
/// listable/selectable provider ("forge-native" / "Forge Accounting Suite") that
/// does NOT pretend to be the GL boundary (the sync surface throws; the GL is
/// reached via <see cref="IPostingEngine"/>). The factory must list it and let it
/// be selected, but selection must NOT change the active provider unless the
/// system setting says so.
/// </summary>
public class ForgeGlAccountingProviderShimTests
{
    private static ForgeGlAccountingService Shim() =>
        new(NullLogger<ForgeGlAccountingService>.Instance);

    [Fact]
    public void Shim_HasNativeIdentity()
    {
        var shim = Shim();
        shim.ProviderId.Should().Be("forge-native");
        ForgeGlAccountingService.Id.Should().Be("forge-native");
        shim.ProviderName.Should().Be("Forge Accounting Suite");
    }

    [Fact]
    public async Task Shim_SyncSurface_ThrowsNotSupported_PointingAtTheRealSeam()
    {
        var shim = Shim();

        // A representative sample of the external-sync contract — all must throw,
        // because the native GL is NOT reached through IAccountingService.
        var customer = new AccountingCustomer("ext-1", "Acme", null, null, null, 0m);
        var createCustomer = async () => await shim.CreateCustomerAsync(customer, CancellationToken.None);
        var getItems = async () => await shim.GetItemsAsync(CancellationToken.None);
        var getCustomers = async () => await shim.GetCustomersAsync(CancellationToken.None);

        (await createCustomer.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("IPostingEngine");
        await getItems.Should().ThrowAsync<NotSupportedException>();
        await getCustomers.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Shim_ConnectivityProbes_ReportHealthyAndSyncFree()
    {
        var shim = Shim();

        (await shim.TestConnectionAsync(CancellationToken.None)).Should().BeTrue();

        var status = await shim.GetSyncStatusAsync(CancellationToken.None);
        status.Connected.Should().BeTrue();
        status.QueueDepth.Should().Be(0);
        status.FailedCount.Should().Be(0);
    }

    [Fact]
    public async Task Factory_ListsForgeNative_AsSelectableButInactiveByDefault()
    {
        // No active provider set → standalone; forge-native is listed but not active.
        var settings = new Mock<ISystemSettingRepository>();
        settings.Setup(s => s.FindByKeyAsync("accounting_provider", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSetting?)null);

        var providers = new IAccountingService[]
        {
            new LocalAccountingService(NullLogger<LocalAccountingService>.Instance),
            Shim(),
        };
        var factory = new AccountingProviderFactory(
            providers, settings.Object, NullLogger<AccountingProviderFactory>.Instance);

        var infos = await factory.GetAvailableProvidersAsync(CancellationToken.None);
        var forgeNative = infos.SingleOrDefault(i => i.Id == "forge-native");

        forgeNative.Should().NotBeNull("forge-native must be a listed/selectable provider (§5.5)");
        forgeNative!.Name.Should().Be("Forge Accounting Suite");
        forgeNative.IsConfigured.Should().BeFalse("it must NOT be active just by being registered");

        // The active provider stays standalone (no system setting).
        (await factory.GetActiveProviderAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Factory_GetProvider_ResolvesForgeNativeWhenRegistered()
    {
        var settings = new Mock<ISystemSettingRepository>();
        var providers = new IAccountingService[] { Shim() };
        var factory = new AccountingProviderFactory(
            providers, settings.Object, NullLogger<AccountingProviderFactory>.Instance);

        factory.GetProvider("forge-native").Should().BeOfType<ForgeGlAccountingService>();

        // Selecting it is allowed (it is registered); this writes the setting but
        // does not magically activate it for callers that don't read the setting.
        await factory.Invoking(f => f.SetActiveProviderAsync("forge-native", CancellationToken.None))
            .Should().NotThrowAsync();
    }
}
