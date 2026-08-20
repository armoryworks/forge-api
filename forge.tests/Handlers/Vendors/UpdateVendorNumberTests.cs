using FluentAssertions;
using Moq;
using Forge.Api.Features.Vendors;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Vendors;

public class UpdateVendorNumberTests
{
    private readonly Mock<IVendorRepository> _vendorRepo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly UpdateVendorHandler _handler;

    public UpdateVendorNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());

        _handler = new UpdateVendorHandler(
            _vendorRepo.Object, _settings.Object, _identifiers.Object, Mock.Of<IClock>());
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("vendors.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "vendors.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoForUpdate(Vendor vendor)
    {
        _vendorRepo.Setup(r => r.FindAsync(vendor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _vendorRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    // Id + all string/decimal/bool fields null, then the number (last positional arg).
    private static UpdateVendorCommand WithVendorNumber(int id, string number) =>
        new(id, null, null, null, null, null, null, null, null, null, null, null, null, null, number);

    [Fact]
    public async Task Renames_the_vendor_number_when_manual_numbers_allowed_and_unique()
    {
        var vendor = new Vendor { Id = 1, CompanyName = "Acme", VendorNumber = "VEND-00001" };
        SetupRepoForUpdate(vendor);
        AllowManualNumbers(true);
        _vendorRepo.Setup(r => r.VendorNumberExistsAsync("ACME-42", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(WithVendorNumber(1, "ACME-42"), CancellationToken.None);

        vendor.VendorNumber.Should().Be("ACME-42");
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.Vendor, 1, "ACME-42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_vendor_number()
    {
        var vendor = new Vendor { Id = 1, CompanyName = "Acme", VendorNumber = "VEND-00001" };
        SetupRepoForUpdate(vendor);
        AllowManualNumbers(true);
        _vendorRepo.Setup(r => r.VendorNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(WithVendorNumber(1, "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        vendor.VendorNumber.Should().Be("VEND-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var vendor = new Vendor { Id = 1, CompanyName = "Acme", VendorNumber = "VEND-00001" };
        SetupRepoForUpdate(vendor);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(WithVendorNumber(1, "ACME-42"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        vendor.VendorNumber.Should().Be("VEND-00001");
    }
}
