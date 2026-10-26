using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Shipping;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipping;

public sealed class PickWaveHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = Mock.Of<IClock>(c => c.UtcNow == Now);

    public void Dispose() => _db.Dispose();

    private async Task<(Part Part, StorageLocation Bin, List<ShipmentLine> Lines)> SeedAsync(
        decimal onHand = 10m, string? lot = "LOT-7", params decimal[] quantities)
    {
        var customer = new Customer { Name = "Buyer Co" };
        var part = new Part { PartNumber = "P-200", Name = "Bracket" };
        var aisle = new StorageLocation { Name = "Aisle 1", LocationType = LocationType.Area, IsActive = true };
        _db.AddRange(customer, part, aisle);
        await _db.SaveChangesAsync();

        var bin = new StorageLocation { Name = "Bin 3", LocationType = LocationType.Bin, ParentId = aisle.Id, IsActive = true };
        _db.Add(bin);
        await _db.SaveChangesAsync();

        if (onHand > 0)
        {
            _db.BinContents.Add(new BinContent
            {
                LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = onHand,
                LotNumber = lot, Status = BinContentStatus.Stored, PlacedAt = Now.AddDays(-3),
            });
        }

        var order = new SalesOrder
        {
            OrderNumber = "SO-1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = Now.AddDays(2),
            Lines = [new SalesOrderLine { PartId = part.Id, Description = "Bracket", Quantity = quantities.Sum(), LineNumber = 1 }],
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        var shipment = new Shipment
        {
            ShipmentNumber = "SH-1", SalesOrderId = order.Id, Status = ShipmentStatus.Pending,
            Lines = quantities.Select(q => new ShipmentLine { SalesOrderLineId = order.Lines.First().Id, PartId = part.Id, Quantity = q }).ToList(),
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (part, bin, shipment.Lines.ToList());
    }

    private Task<PickWaveResponseModel> Create(params int[] shipmentLineIds) =>
        new CreatePickWaveHandler(_db).Handle(
            new CreatePickWaveCommand(new CreatePickWaveRequestModel { ShipmentLineIds = [.. shipmentLineIds] }),
            CancellationToken.None);

    private Task Release(int waveId) =>
        new ReleasePickWaveHandler(_db, _clock).Handle(new ReleasePickWaveCommand(waveId), CancellationToken.None);

    private Task Confirm(int waveId, int lineId, decimal qty, string? notes = null) =>
        new ConfirmPickLineHandler(_db, _clock).Handle(
            new ConfirmPickLineCommand(waveId, lineId, new ConfirmPickLineRequestModel { PickedQuantity = qty, ShortNotes = notes }),
            CancellationToken.None);

    private Task<PickWaveResponseModel> Get(int waveId) =>
        new GetPickWaveHandler(_db).Handle(new GetPickWaveQuery(waveId), CancellationToken.None);

    [Fact]
    public async Task A_new_wave_sources_each_line_from_the_stocked_bin_with_its_path_and_lot()
    {
        var (_, bin, lines) = await SeedAsync(10m, "LOT-7", 4m);

        var wave = await Create(lines[0].Id);
        var detail = await Get(wave.Id);

        detail.Status.Should().Be(PickWaveStatus.Draft);
        detail.TotalLines.Should().Be(1);
        var line = detail.Lines.Should().ContainSingle().Subject;
        line.FromLocationName.Should().Be("Bin 3");
        line.BinPath.Should().Be("Aisle 1 / Bin 3");
        line.LotNumber.Should().Be("LOT-7");
        line.ShipmentNumber.Should().Be("SH-1");
        line.RequestedQuantity.Should().Be(4m);
        (await _db.PickLines.SingleAsync()).FromLocationId.Should().Be(bin.Id);
    }

    [Fact]
    public async Task A_part_with_no_stock_falls_back_to_the_default_location()
    {
        var (_, _, lines) = await SeedAsync(0m, null, 2m);
        var dock = new StorageLocation { Name = "Dock", LocationType = LocationType.Area, IsActive = true, IsDefault = true };
        _db.Add(dock);
        await _db.SaveChangesAsync();

        var wave = await Create(lines[0].Id);

        (await Get(wave.Id)).Lines.Single().FromLocationName.Should().Be("Dock");
    }

    [Fact]
    public async Task A_line_already_in_a_live_wave_is_not_waved_twice()
    {
        var (_, _, lines) = await SeedAsync(10m, null, 2m);
        await Create(lines[0].Id);

        var act = () => Create(lines[0].Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Auto_generate_takes_open_lines_inside_the_ship_by_window()
    {
        await SeedAsync(10m, null, 2m, 3m);
        var handler = new AutoGeneratePickWaveHandler(_db);

        var outside = () => handler.Handle(new AutoGeneratePickWaveCommand(new AutoWaveParametersModel
        {
            ShipByDateTo = Now.AddDays(1),
        }), CancellationToken.None);
        await outside.Should().ThrowAsync<InvalidOperationException>();

        var wave = await handler.Handle(new AutoGeneratePickWaveCommand(new AutoWaveParametersModel
        {
            ShipByDateTo = Now.AddDays(3),
        }), CancellationToken.None);

        wave.TotalLines.Should().Be(2);
        (await Get(wave.Id)).Lines.Select(l => l.SortOrder).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Picking_before_release_is_refused()
    {
        var (_, _, lines) = await SeedAsync(10m, null, 2m);
        var wave = await Create(lines[0].Id);
        var lineId = (await _db.PickLines.SingleAsync()).Id;

        var act = () => Confirm(wave.Id, lineId, 2m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Release the wave*");
    }

    [Fact]
    public async Task A_short_pick_records_the_picker_and_starts_the_wave()
    {
        var (_, _, lines) = await SeedAsync(10m, null, 5m);
        var wave = await Create(lines[0].Id);
        var lineId = (await _db.PickLines.SingleAsync()).Id;
        await Release(wave.Id);
        _db.CurrentUserId = 42;

        await Confirm(wave.Id, lineId, 3m, "  bin short  ");

        var detail = await Get(wave.Id);
        detail.Status.Should().Be(PickWaveStatus.InProgress);
        detail.StartedAt.Should().Be(Now);
        detail.PickedLines.Should().Be(1);
        var line = detail.Lines.Single();
        line.Status.Should().Be(PickLineStatus.Short);
        line.PickedQuantity.Should().Be(3m);
        line.PickedByUserId.Should().Be(42);
        line.ShortNotes.Should().Be("bin short");
        (await _db.ActivityLogs.Where(a => a.EntityType == "PickWave" && a.EntityId == wave.Id).Select(a => a.Action).ToListAsync())
            .Should().Contain(["created", "released", "pick-line-short"]);
    }

    [Fact]
    public async Task Picking_more_than_requested_is_refused()
    {
        var (_, _, lines) = await SeedAsync(10m, null, 2m);
        var wave = await Create(lines[0].Id);
        var lineId = (await _db.PickLines.SingleAsync()).Id;
        await Release(wave.Id);

        var act = () => Confirm(wave.Id, lineId, 3m);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void A_negative_pick_fails_validation()
    {
        new ConfirmPickLineValidator()
            .TestValidate(new ConfirmPickLineCommand(1, 1, new ConfirmPickLineRequestModel { PickedQuantity = -1m }))
            .ShouldHaveValidationErrorFor(x => x.Request.PickedQuantity);
    }

    [Fact]
    public async Task Completing_a_wave_skips_the_lines_left_unpicked()
    {
        var (_, _, lines) = await SeedAsync(10m, null, 2m, 1m);
        var wave = await Create(lines[0].Id, lines[1].Id);
        await Release(wave.Id);
        var first = (await Get(wave.Id)).Lines.First();
        await Confirm(wave.Id, first.Id, first.RequestedQuantity);

        await new CompletePickWaveHandler(_db, _clock).Handle(new CompletePickWaveCommand(wave.Id), CancellationToken.None);

        var detail = await Get(wave.Id);
        detail.Status.Should().Be(PickWaveStatus.Completed);
        detail.PickedLines.Should().Be(1);
        detail.Lines.Select(l => l.Status).Should().BeEquivalentTo([PickLineStatus.Picked, PickLineStatus.Skipped]);
    }

    [Fact]
    public async Task My_pick_lines_are_the_open_lines_of_released_waves_mine_or_unassigned()
    {
        var (_, _, lines) = await SeedAsync(10m, null, 1m, 1m, 1m);
        var mine = await Create(lines[0].Id);
        var unassigned = await Create(lines[1].Id);
        var draft = await Create(lines[2].Id);
        (await _db.PickWaves.SingleAsync(w => w.Id == mine.Id)).AssignedToId = 7;
        await _db.SaveChangesAsync();
        await Release(mine.Id);
        await Release(unassigned.Id);

        var forSeven = await new GetMyPickLinesHandler(_db).Handle(new GetMyPickLinesQuery(7), CancellationToken.None);
        var forEight = await new GetMyPickLinesHandler(_db).Handle(new GetMyPickLinesQuery(8), CancellationToken.None);

        forSeven.Select(l => l.WaveId).Should().BeEquivalentTo([mine.Id, unassigned.Id]);
        forEight.Select(l => l.WaveId).Should().BeEquivalentTo([unassigned.Id]);
        forSeven.Should().NotContain(l => l.WaveId == draft.Id);
    }

    [Fact]
    public async Task The_wave_list_names_the_assignee_last_name_first()
    {
        var (_, _, lines) = await SeedAsync(10m, null, 1m);
        _db.Users.Add(new ApplicationUser { Id = 9, UserName = "pat", FirstName = "Pat", LastName = "Picker" });
        await _db.SaveChangesAsync();
        await new CreatePickWaveHandler(_db).Handle(new CreatePickWaveCommand(new CreatePickWaveRequestModel
        {
            ShipmentLineIds = [lines[0].Id], AssignedToId = 9,
        }), CancellationToken.None);

        var list = await new GetPickWavesHandler(_db).Handle(new GetPickWavesQuery(null, null), CancellationToken.None);

        list.Single().AssignedToName.Should().Be("Picker, Pat");
    }
}
