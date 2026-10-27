using FluentAssertions;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Vendors;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Vendors;

public class VendorAutoPoModeTests
{
    private readonly Mock<IVendorRepository> _vendorRepo = new();

    private UpdateVendorHandler Handler(ICapabilitySnapshotProvider capabilities) => new(
        _vendorRepo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
        TestDbContextFactory.Create(), Mock.Of<IClock>(), capabilities);

    private Vendor Seed(AutoPoMode? mode)
    {
        var vendor = new Vendor { Id = 7, CompanyName = "Acme", AutoPoMode = mode };
        _vendorRepo.Setup(r => r.FindAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        return vendor;
    }

    private static UpdateVendorCommand Command(AutoPoMode? mode = null, bool? clear = null) =>
        new(7, null, null, null, null, null, null, null, null, null, null, null, null, null,
            AutoPoMode: mode, ClearAutoPoMode: clear);

    [Fact]
    public async Task Update_sets_the_vendor_override()
    {
        var vendor = Seed(null);

        await Handler(new StubCapabilitySnapshotProvider("CAP-P2P-AUTOPO"))
            .Handle(Command(AutoPoMode.Automatic), CancellationToken.None);

        vendor.AutoPoMode.Should().Be(AutoPoMode.Automatic);
    }

    [Fact]
    public async Task Clear_returns_the_vendor_to_the_global_default()
    {
        var vendor = Seed(AutoPoMode.Suggest);

        await Handler(new StubCapabilitySnapshotProvider("CAP-P2P-AUTOPO"))
            .Handle(Command(clear: true), CancellationToken.None);

        vendor.AutoPoMode.Should().BeNull();
    }

    [Fact]
    public async Task Omitting_the_field_leaves_the_override_alone_even_with_the_capability_off()
    {
        var vendor = Seed(AutoPoMode.Draft);

        await Handler(StubCapabilitySnapshotProvider.Off).Handle(Command(), CancellationToken.None);

        vendor.AutoPoMode.Should().Be(AutoPoMode.Draft);
    }

    [Fact]
    public async Task Setting_the_override_with_auto_po_off_is_refused()
    {
        var vendor = Seed(null);

        var act = () => Handler(StubCapabilitySnapshotProvider.Off)
            .Handle(Command(AutoPoMode.Automatic), CancellationToken.None);

        await act.Should().ThrowAsync<CapabilityDisabledException>();
        vendor.AutoPoMode.Should().BeNull();
    }

    [Fact]
    public void Sending_a_mode_and_clear_together_is_invalid()
    {
        new UpdateVendorValidator().Validate(Command(AutoPoMode.Draft, clear: true)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Detail_returns_the_override()
    {
        _vendorRepo.Setup(r => r.FindWithDetailsAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Vendor { Id = 7, CompanyName = "Acme", AutoPoMode = AutoPoMode.Suggest });

        var result = await new GetVendorByIdHandler(_vendorRepo.Object)
            .Handle(new GetVendorByIdQuery(7), CancellationToken.None);

        result.AutoPoMode.Should().Be("Suggest");
    }
}
