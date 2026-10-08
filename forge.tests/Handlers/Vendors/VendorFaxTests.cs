using FluentAssertions;
using Moq;

using Forge.Api.Features.Vendors;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Vendors;

public class VendorFaxTests
{
    private readonly Mock<IVendorRepository> _vendorRepo = new();

    [Fact]
    public async Task Create_stores_the_fax_number()
    {
        var handler = new CreateVendorHandler(
            _vendorRepo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>());

        await handler.Handle(
            new CreateVendorCommand("Acme", null, null, null, null, null, null, null, null, null, null, Fax: "555-0100"),
            CancellationToken.None);

        _vendorRepo.Verify(r => r.AddAsync(It.Is<Vendor>(v => v.Fax == "555-0100"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_changes_the_fax_and_leaves_it_alone_when_omitted()
    {
        var vendor = new Vendor { Id = 1, CompanyName = "Acme", Fax = "555-0100" };
        _vendorRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        var handler = new UpdateVendorHandler(
            _vendorRepo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
            TestDbContextFactory.Create(), Mock.Of<IClock>());

        await handler.Handle(
            new UpdateVendorCommand(1, null, null, null, null, null, null, null, null, null, null, null, null, null),
            CancellationToken.None);
        vendor.Fax.Should().Be("555-0100");

        await handler.Handle(
            new UpdateVendorCommand(1, null, null, null, null, null, null, null, null, null, null, null, null, null, Fax: "555-0199"),
            CancellationToken.None);
        vendor.Fax.Should().Be("555-0199");
    }

    [Fact]
    public async Task Detail_returns_the_fax_number()
    {
        _vendorRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Vendor { Id = 1, CompanyName = "Acme", Fax = "555-0100" });

        var result = await new GetVendorByIdHandler(_vendorRepo.Object)
            .Handle(new GetVendorByIdQuery(1), CancellationToken.None);

        result.Fax.Should().Be("555-0100");
    }

    [Fact]
    public void Validators_reject_a_fax_longer_than_the_column()
    {
        var tooLong = new string('5', 51);

        new CreateVendorValidator()
            .Validate(new CreateVendorCommand("Acme", null, null, null, null, null, null, null, null, null, null, Fax: tooLong))
            .IsValid.Should().BeFalse();
        new UpdateVendorValidator()
            .Validate(new UpdateVendorCommand(1, null, null, null, null, null, null, null, null, null, null, null, null, null, Fax: tooLong))
            .IsValid.Should().BeFalse();
    }
}
