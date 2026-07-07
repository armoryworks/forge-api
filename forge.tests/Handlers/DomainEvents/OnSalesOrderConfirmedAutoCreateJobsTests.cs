using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.DomainEvents;

/// <summary>
/// Jobs auto-created from a confirmed SO must start at the order_confirmed
/// stage — the Sales Orders surface's entry point — so the confirmed order
/// stays visible on the SO list. Tracks without that stage code fall back to
/// the first active stage.
/// </summary>
public class OnSalesOrderConfirmedAutoCreateJobsTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<ITrackTypeRepository> _trackRepo = new();
    private readonly Mock<IBarcodeService> _barcodes = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();

    private readonly List<Job> _addedJobs = [];

    public OnSalesOrderConfirmedAutoCreateJobsTests()
    {
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("J-9001");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _jobRepo.Setup(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((j, _) => _addedJobs.Add(j))
            .Returns(Task.CompletedTask);
        _jobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        _boardHub.SetupGet(h => h.Clients).Returns(clients.Object);
    }

    private OnSalesOrderConfirmed_AutoCreateJobs Handler() => new(
        _db, _jobRepo.Object, _trackRepo.Object, _barcodes.Object, _boardHub.Object,
        NullLogger<OnSalesOrderConfirmed_AutoCreateJobs>.Instance);

    private async Task<SalesOrder> SeedAsync(bool withOrderConfirmedStage)
    {
        var track = new TrackType { Id = 7, Name = "Production", IsDefault = true, IsActive = true };
        _db.TrackTypes.Add(track);
        _db.JobStages.Add(new JobStage
        {
            Id = 70, TrackTypeId = 7, Name = "Quote Requested", Code = "quote_requested",
            SortOrder = 1, IsActive = true,
        });
        if (withOrderConfirmedStage)
        {
            _db.JobStages.Add(new JobStage
            {
                Id = 73, TrackTypeId = 7, Name = "Order Confirmed", Code = "order_confirmed",
                SortOrder = 3, IsActive = true,
            });
        }

        var customer = new Customer { Id = 1, Name = "Acme" };
        _db.Customers.Add(customer);
        var so = new SalesOrder
        {
            Id = 501, OrderNumber = "SO-00001", CustomerId = 1, Customer = customer,
            Status = SalesOrderStatus.Confirmed,
            Lines = { new SalesOrderLine { Id = 601, Description = "Widget", Quantity = 2m, UnitPrice = 5m, LineNumber = 1 } },
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        return so;
    }

    [Fact]
    public async Task Auto_created_job_starts_at_order_confirmed_stage()
    {
        var so = await SeedAsync(withOrderConfirmedStage: true);

        await Handler().Handle(new SalesOrderConfirmedEvent(so.Id, 1), CancellationToken.None);

        _addedJobs.Should().ContainSingle();
        _addedJobs[0].CurrentStageId.Should().Be(73,
            "a job born from a confirmed order belongs at order_confirmed, the SO surface's entry point — " +
            "starting lower makes the confirmed SO invisible on the Sales Orders list");
        _addedJobs[0].SalesOrderLineId.Should().Be(601);
    }

    [Fact]
    public async Task Track_without_order_confirmed_stage_falls_back_to_first_active_stage()
    {
        var so = await SeedAsync(withOrderConfirmedStage: false);
        _trackRepo.Setup(r => r.FindFirstActiveStageAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(await _db.JobStages.FindAsync(70));

        await Handler().Handle(new SalesOrderConfirmedEvent(so.Id, 1), CancellationToken.None);

        _addedJobs.Should().ContainSingle();
        _addedJobs[0].CurrentStageId.Should().Be(70);
    }
}
