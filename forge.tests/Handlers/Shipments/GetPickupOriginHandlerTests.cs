using FluentAssertions;

using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

public sealed class GetPickupOriginHandlerTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private Task<Forge.Core.Models.PickupOriginResponseModel> Get() =>
        new GetPickupOriginHandler(_db).Handle(new GetPickupOriginQuery(), CancellationToken.None);

    [Fact]
    public async Task The_origin_is_the_default_location_in_its_plant_time_zone()
    {
        var main = new CompanyLocation { Name = "Main plant", IsDefault = true, IsActive = true };
        var other = new CompanyLocation { Name = "Annex", IsActive = true };
        _db.AddRange(main, other);
        await _db.SaveChangesAsync();
        _db.Plants.AddRange(
            new Plant { Code = "AX", Name = "Annex", CompanyLocationId = other.Id, TimeZone = "America/New_York", IsActive = true },
            new Plant { Code = "MP", Name = "Main", CompanyLocationId = main.Id, TimeZone = "America/Denver", IsActive = true });
        await _db.SaveChangesAsync();

        var origin = await Get();

        origin.LocationName.Should().Be("Main plant");
        origin.TimeZone.Should().Be("America/Denver");
    }

    [Fact]
    public async Task Without_a_plant_zone_the_client_zone_is_used()
    {
        _db.Add(new CompanyLocation { Name = "Main plant", IsDefault = true, IsActive = true });
        await _db.SaveChangesAsync();

        (await Get()).TimeZone.Should().BeNull();
    }

    [Fact]
    public async Task Without_a_default_location_nothing_is_known()
    {
        (await Get()).Should().Be(new Forge.Core.Models.PickupOriginResponseModel(null, null));
    }
}
