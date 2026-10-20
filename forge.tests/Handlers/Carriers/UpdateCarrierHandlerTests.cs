using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Carriers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Carriers;

public class UpdateCarrierHandlerTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private async Task<Carrier> SeedAsync()
    {
        var carrier = new Carrier
        {
            Name = "UPS",
            Code = "UPS",
            IntegrationKind = CarrierIntegrationKind.Api,
            IntegrationServiceId = "ups",
            DeliveryUpdateMode = CarrierDeliveryUpdateMode.Manual,
            RequiresScanToShip = true,
        };
        _db.Carriers.AddRange(carrier, new Carrier { Name = "House courier", Code = "HOUSE" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return carrier;
    }

    private static UpdateCarrierCommand Command(int id,
        string? name = null, string? code = null, string? integrationKind = null, string? deliveryUpdateMode = null,
        string? integrationServiceId = null, bool? requiresScanToShip = null, bool? isActive = null) =>
        new(id, name, code, null, integrationKind, deliveryUpdateMode, integrationServiceId,
            requiresScanToShip, isActive, null, null);

    [Fact]
    public async Task Changes_only_the_fields_sent_and_logs_one_activity_row()
    {
        var carrier = await SeedAsync();

        var result = await new UpdateCarrierHandler(_db).Handle(
            Command(carrier.Id, deliveryUpdateMode: "Webhook", requiresScanToShip: false), CancellationToken.None);

        result.DeliveryUpdateMode.Should().Be("Webhook");
        result.RequiresScanToShip.Should().BeFalse();
        result.Name.Should().Be("UPS");
        result.IntegrationKind.Should().Be("Api");

        _db.ChangeTracker.Clear();
        var stored = await _db.Carriers.SingleAsync(c => c.Id == carrier.Id);
        stored.DeliveryUpdateMode.Should().Be(CarrierDeliveryUpdateMode.Webhook);
        stored.RequiresScanToShip.Should().BeFalse();
        var log = await _db.ActivityLogs.SingleAsync(a => a.EntityType == "Carrier" && a.EntityId == carrier.Id && a.Action == "updated");
        log.Description.Should().Contain("delivery update mode").And.Contain("requires scan to ship");
    }

    [Fact]
    public async Task Turns_an_api_carrier_into_a_manual_one_and_deactivates_it()
    {
        var carrier = await SeedAsync();

        var result = await new UpdateCarrierHandler(_db).Handle(
            Command(carrier.Id, integrationKind: "Manual", isActive: false), CancellationToken.None);

        result.IntegrationKind.Should().Be("Manual");
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Refuses_a_code_another_carrier_already_uses()
    {
        var carrier = await SeedAsync();

        var act = () => new UpdateCarrierHandler(_db).Handle(Command(carrier.Id, code: "HOUSE"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*HOUSE*");
    }

    [Fact]
    public async Task Refuses_an_api_carrier_without_an_integration_service()
    {
        var carrier = await SeedAsync();

        var act = () => new UpdateCarrierHandler(_db).Handle(
            Command(carrier.Id, integrationServiceId: ""), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integration service*");
    }

    [Fact]
    public async Task Writes_nothing_when_no_field_changes()
    {
        var carrier = await SeedAsync();

        await new UpdateCarrierHandler(_db).Handle(Command(carrier.Id, name: "UPS"), CancellationToken.None);

        (await _db.ActivityLogs.CountAsync(a => a.Action == "updated")).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_carrier_is_not_found()
    {
        var act = () => new UpdateCarrierHandler(_db).Handle(Command(999, name: "X"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void Validator_rejects_an_unknown_delivery_mode_and_a_blank_name()
    {
        var validator = new UpdateCarrierValidator();

        validator.Validate(Command(1, deliveryUpdateMode: "Carrier pigeon")).IsValid.Should().BeFalse();
        validator.Validate(Command(1, name: " ")).IsValid.Should().BeFalse();
        validator.Validate(Command(1, deliveryUpdateMode: "poll")).IsValid.Should().BeTrue();
    }
}
