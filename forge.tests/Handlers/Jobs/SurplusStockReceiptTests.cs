using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class SurplusStockReceiptTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<ICapabilitySnapshotProvider> _capabilities = new();
    private readonly Mock<ISettingsService> _settings = new();
    private string? _surplusSetting;

    public SurplusStockReceiptTests()
    {
        _capabilities.Setup(c => c.IsEnabled(ReceiveProductionRunToStockHandler.MultiLocationCapability)).Returns(true);
        _capabilities.Setup(c => c.IsEnabled(DisposeJobHandler.StockingCapability)).Returns(true);
        _settings.Setup(s => s.GetStringAsync(InventorySettings.SurplusLocationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _surplusSetting);
    }

    private ReceiveProductionRunToStockHandler ReceiveHandler() => new(
        _db, new SystemClock(), _capabilities.Object, new InventoryRepository(_db), posting: null, settings: _settings.Object);

    private async Task<StorageLocation> BinAsync(string name, bool active = true)
    {
        var bin = new StorageLocation { Name = name, LocationType = LocationType.Bin, IsActive = active };
        _db.StorageLocations.Add(bin);
        await _db.SaveChangesAsync();
        return bin;
    }

    private async Task<Job> JobAsync(bool forOrder, decimal start, decimal? target)
    {
        var part = new Part { PartNumber = "BAR-114", Name = "Pin", InventoryClass = InventoryClass.FinishedGood };
        _db.Parts.Add(part);
        int? lineId = null;
        if (forOrder)
        {
            var order = new SalesOrder { OrderNumber = "SO-900", CustomerId = 1, Status = SalesOrderStatus.Confirmed };
            _db.SalesOrders.Add(order);
            await _db.SaveChangesAsync();
            var line = new SalesOrderLine
            {
                SalesOrderId = order.Id, PartId = part.Id, Description = "Pins", Quantity = target ?? start,
                UnitPrice = 2m, LineNumber = 1,
            };
            _db.SalesOrderLines.Add(line);
            await _db.SaveChangesAsync();
            lineId = line.Id;
        }

        var job = new Job
        {
            JobNumber = "JOB-0900", Title = "Pins", TrackTypeId = 1, CurrentStageId = 1, PartId = part.Id,
            SalesOrderLineId = lineId, TargetQuantity = target,
            JobParts = [new JobPart { PartId = part.Id, Quantity = start }],
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private async Task<ProductionRun> RunAsync(Job job, int good, string number = "RUN-900")
    {
        var run = new ProductionRun
        {
            JobId = job.Id, PartId = job.PartId!.Value, RunNumber = number, TargetQuantity = good,
            CompletedQuantity = good, Status = ProductionRunStatus.Completed, CompletedAt = DateTimeOffset.UtcNow,
        };
        _db.ProductionRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    private async Task<Dictionary<int, decimal>> StockByBinAsync(int partId)
    {
        _db.ChangeTracker.Clear();
        var rows = await _db.BinContents.Where(b => b.EntityType == "part" && b.EntityId == partId).ToListAsync();
        return rows.GroupBy(b => b.LocationId).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
    }

    [Fact]
    public async Task A_run_for_an_order_puts_the_order_quantity_in_the_chosen_bin_and_the_rest_in_the_surplus_bin()
    {
        var main = await BinAsync("A-01");
        var surplus = await BinAsync("SURPLUS");
        _surplusSetting = surplus.Id.ToString();
        var job = await JobAsync(forOrder: true, start: 114m, target: 100m);
        var run = await RunAsync(job, 114);

        await ReceiveHandler().Handle(new ReceiveProductionRunToStockCommand(job.Id, run.Id, 1, main.Id), default);

        (await StockByBinAsync(job.PartId!.Value)).Should().BeEquivalentTo(
            new Dictionary<int, decimal> { [main.Id] = 100m, [surplus.Id] = 14m });
        var movements = await _db.BinMovements.Where(m => m.EntityId == job.PartId).ToListAsync();
        movements.Should().HaveCount(2).And.OnlyContain(m => m.Reason == BinMovementReason.Receive);
        movements.Sum(m => m.Quantity).Should().Be(114m);
        (await _db.ProductionRuns.SingleAsync()).ReceivedQuantity.Should().Be(114);
        (await _db.JobActivityLogs.SingleAsync()).Description.Should()
            .Be("Received 114 of BAR-114 from run RUN-900 into A-01. 14 beyond the order quantity went to the surplus location SURPLUS.");
    }

    [Fact]
    public async Task With_the_setting_off_everything_goes_to_the_chosen_bin()
    {
        var main = await BinAsync("A-01");
        await BinAsync("SURPLUS");
        var job = await JobAsync(forOrder: true, start: 114m, target: 100m);
        var run = await RunAsync(job, 114);

        await ReceiveHandler().Handle(new ReceiveProductionRunToStockCommand(job.Id, run.Id, 1, main.Id), default);

        (await StockByBinAsync(job.PartId!.Value)).Should().BeEquivalentTo(new Dictionary<int, decimal> { [main.Id] = 114m });
    }

    [Fact]
    public async Task A_surplus_setting_naming_an_inactive_bin_is_treated_as_off()
    {
        var main = await BinAsync("A-01");
        var inactive = await BinAsync("OLD-SURPLUS", active: false);
        _surplusSetting = inactive.Id.ToString();
        var job = await JobAsync(forOrder: true, start: 114m, target: 100m);
        var run = await RunAsync(job, 114);

        await ReceiveHandler().Handle(new ReceiveProductionRunToStockCommand(job.Id, run.Id, 1, main.Id), default);

        (await StockByBinAsync(job.PartId!.Value)).Should().BeEquivalentTo(new Dictionary<int, decimal> { [main.Id] = 114m });
    }

    [Fact]
    public async Task A_make_to_stock_job_is_never_split()
    {
        var main = await BinAsync("A-01");
        var surplus = await BinAsync("SURPLUS");
        _surplusSetting = surplus.Id.ToString();
        var job = await JobAsync(forOrder: false, start: 114m, target: 100m);
        var run = await RunAsync(job, 114);

        await ReceiveHandler().Handle(new ReceiveProductionRunToStockCommand(job.Id, run.Id, 1, main.Id), default);

        (await StockByBinAsync(job.PartId!.Value)).Should().BeEquivalentTo(new Dictionary<int, decimal> { [main.Id] = 114m });
    }

    [Fact]
    public async Task A_later_run_after_the_order_is_covered_goes_entirely_to_surplus()
    {
        var main = await BinAsync("A-01");
        var surplus = await BinAsync("SURPLUS");
        _surplusSetting = surplus.Id.ToString();
        var job = await JobAsync(forOrder: true, start: 114m, target: 100m);
        var first = await RunAsync(job, 96, "RUN-901");
        var second = await RunAsync(job, 18, "RUN-902");

        await ReceiveHandler().Handle(new ReceiveProductionRunToStockCommand(job.Id, first.Id, 1, main.Id), default);
        await ReceiveHandler().Handle(new ReceiveProductionRunToStockCommand(job.Id, second.Id, 1, main.Id), default);

        (await StockByBinAsync(job.PartId!.Value)).Should().BeEquivalentTo(
            new Dictionary<int, decimal> { [main.Id] = 100m, [surplus.Id] = 14m });
    }

    [Fact]
    public async Task The_operator_can_send_the_surplus_to_another_bin()
    {
        var main = await BinAsync("A-01");
        var surplus = await BinAsync("SURPLUS");
        var retail = await BinAsync("RETAIL");
        _surplusSetting = surplus.Id.ToString();
        var job = await JobAsync(forOrder: true, start: 114m, target: 100m);
        var run = await RunAsync(job, 114);

        await ReceiveHandler().Handle(
            new ReceiveProductionRunToStockCommand(job.Id, run.Id, 1, main.Id, SurplusLocationId: retail.Id), default);

        (await StockByBinAsync(job.PartId!.Value)).Should().BeEquivalentTo(
            new Dictionary<int, decimal> { [main.Id] = 100m, [retail.Id] = 14m });
    }

    [Fact]
    public async Task A_job_without_a_target_uses_its_quantity_as_the_order_quantity()
    {
        var main = await BinAsync("A-01");
        var surplus = await BinAsync("SURPLUS");
        _surplusSetting = surplus.Id.ToString();
        var job = await JobAsync(forOrder: true, start: 100m, target: null);
        var run = await RunAsync(job, 103);

        await ReceiveHandler().Handle(new ReceiveProductionRunToStockCommand(job.Id, run.Id, 1, main.Id), default);

        (await StockByBinAsync(job.PartId!.Value)).Should().BeEquivalentTo(
            new Dictionary<int, decimal> { [main.Id] = 100m, [surplus.Id] = 3m });
    }

    [Fact]
    public async Task Disposing_an_order_job_to_inventory_splits_the_good_quantity()
    {
        var main = await BinAsync("A-01");
        var surplus = await BinAsync("SURPLUS");
        _surplusSetting = surplus.Id.ToString();
        var job = await JobAsync(forOrder: true, start: 114m, target: 100m);

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        jobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns<CancellationToken>(ct => _db.SaveChangesAsync(ct));
        var receive = ReceiveHandler();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ReceiveProductionRunToStockCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ProductionRunResponseModel> c, CancellationToken ct) => receive.Handle((ReceiveProductionRunToStockCommand)c, ct));
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        var posting = new Mock<IProductionVariancePostingService>();
        posting.Setup(p => p.CloseDisposedJobProductionCostAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, DateOnly _, int _, CancellationToken _) => new JobProductionCostCloseResult(id, 0m, 0m, 0m, Posted: false));
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var dispose = new DisposeJobHandler(
            jobRepo.Object, Mock.Of<IAssetRepository>(), mediator.Object, hub.Object, clock.Object,
            _capabilities.Object, _db, posting.Object);

        await dispose.Handle(new DisposeJobCommand(job.Id,
            new DisposeJobRequestModel(JobDisposition.AddToInventory, null, 114m, main.Id), 1), default);

        (await StockByBinAsync(job.PartId!.Value)).Should().BeEquivalentTo(
            new Dictionary<int, decimal> { [main.Id] = 100m, [surplus.Id] = 14m });
    }

    [Fact]
    public async Task The_split_preview_matches_what_a_receipt_would_do()
    {
        var surplus = await BinAsync("SURPLUS");
        _surplusSetting = surplus.Id.ToString();
        var job = await JobAsync(forOrder: true, start: 114m, target: 100m);
        var handler = new GetJobSurplusSplitHandler(_db, _settings.Object);

        var split = await handler.Handle(new GetJobSurplusSplitQuery(job.Id, 114m), default);
        _surplusSetting = null;
        var off = await handler.Handle(new GetJobSurplusSplitQuery(job.Id, 114m), default);

        split.Should().Be(new JobSurplusSplitResponseModel(100m, 14m, surplus.Id, "SURPLUS"));
        off.Should().Be(new JobSurplusSplitResponseModel(114m, 0m, null, null));
    }

    [Fact]
    public async Task The_surplus_location_lookup_reports_the_configured_bin()
    {
        var surplus = await BinAsync("SURPLUS");
        var handler = new GetSurplusLocationHandler(_db, _settings.Object);

        var off = await handler.Handle(new GetSurplusLocationQuery(), default);
        _surplusSetting = surplus.Id.ToString();
        var on = await handler.Handle(new GetSurplusLocationQuery(), default);

        off.Should().Be(new SurplusLocationResponseModel(null, null));
        on.Should().Be(new SurplusLocationResponseModel(surplus.Id, "SURPLUS"));
    }

    [Fact]
    public void The_surplus_setting_is_in_the_inventory_catalog_group()
        => SettingDescriptorCatalog.FindByKey(InventorySettings.SurplusLocationKey)!.Group.Should().Be("Inventory");
}
