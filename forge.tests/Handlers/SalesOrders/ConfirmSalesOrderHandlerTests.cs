using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class ConfirmSalesOrderHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ISalesOrderAcceptanceGate> _acceptanceGate = new();
    private readonly Mock<IClock> _clock = new();
    private readonly IHttpContextAccessor _httpContext = new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "test")),
        },
    };

    public ConfirmSalesOrderHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private ConfirmSalesOrderHandler Handler() => new(
        new SalesOrderRepository(_db), _db, _mediator.Object, _httpContext, _acceptanceGate.Object, _clock.Object);

    private void SeedTrack()
    {
        _db.TrackTypes.Add(new TrackType { Id = 7, Name = "Production", IsDefault = true, IsActive = true });
        _db.JobStages.Add(new JobStage
        {
            Id = 73, TrackTypeId = 7, Name = "Order Confirmed", Code = "order_confirmed",
            SortOrder = 3, IsActive = true,
        });
    }

    private async Task<SalesOrder> SeedDraftOrderAsync(Customer customer)
    {
        _db.Customers.Add(customer);
        _db.Parts.Add(new Part { Id = 900, PartNumber = "CW-1001", Name = "Clutch weight", ProcurementSource = ProcurementSource.Make });
        _db.Parts.Add(new Part { Id = 901, PartNumber = "40-1700M", Name = "Bracket", ProcurementSource = ProcurementSource.Make });
        var so = new SalesOrder
        {
            Id = 501, OrderNumber = "SO-00001", CustomerId = customer.Id, Status = SalesOrderStatus.Draft,
            Lines =
            {
                new SalesOrderLine { Id = 601, LineNumber = 1, PartId = 900, Quantity = 50m, UnitPrice = 5m, Description = "Clutch weight" },
                new SalesOrderLine { Id = 602, LineNumber = 2, Quantity = 1m, UnitPrice = 40m, Description = "Freight" },
                new SalesOrderLine { Id = 603, LineNumber = 3, PartId = 901, Quantity = 250m, UnitPrice = 2m, Description = "Bracket" },
            },
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        return so;
    }

    [Fact]
    public async Task Customer_on_credit_hold_refuses_with_the_hold_reason()
    {
        SeedTrack();
        var so = await SeedDraftOrderAsync(new Customer
        {
            Id = 1, Name = "Acme", IsOnCreditHold = true, CreditHoldReason = "90 days past due",
        });

        var act = () => Handler().Handle(new ConfirmSalesOrderCommand(so.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Acme is on credit hold: 90 days past due. Release the hold before confirming this order.");
        (await _db.SalesOrders.AsNoTracking().SingleAsync()).Status.Should().Be(SalesOrderStatus.Draft);
        _mediator.Verify(m => m.Publish(It.IsAny<SalesOrderConfirmedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_production_track_refuses()
    {
        var so = await SeedDraftOrderAsync(new Customer { Id = 1, Name = "Acme" });

        var act = () => Handler().Handle(new ConfirmSalesOrderCommand(so.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No production track is set up, so no work orders can be created. Set one up in Admin before confirming.");
        (await _db.SalesOrders.AsNoTracking().SingleAsync()).Status.Should().Be(SalesOrderStatus.Draft);
    }

    [Fact]
    public async Task Confirm_reports_how_many_jobs_the_confirmation_created()
    {
        SeedTrack();
        var so = await SeedDraftOrderAsync(new Customer { Id = 1, Name = "Acme" });

        var jobRepo = new Mock<IJobRepository>();
        var nextJobNumber = 1;
        jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"J-{nextJobNumber++}");
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.SetupGet(h => h.Clients).Returns(hubClients.Object);
        var createJobs = new CreateJobsForSalesOrderLinesHandler(
            _db, jobRepo.Object, Mock.Of<IBarcodeService>(), Mock.Of<IBusinessIdentifierService>(),
            boardHub.Object, _acceptanceGate.Object, Mock.Of<IMediator>(), Mock.Of<ICloudFolderAutoCreator>());
        _mediator.Setup(m => m.Publish(It.IsAny<SalesOrderConfirmedEvent>(), It.IsAny<CancellationToken>()))
            .Returns<SalesOrderConfirmedEvent, CancellationToken>((e, ct) =>
                createJobs.Handle(new CreateJobsForSalesOrderLinesCommand(e.SalesOrderId, FromConfirmation: true), ct));

        var result = await Handler().Handle(new ConfirmSalesOrderCommand(so.Id), CancellationToken.None);

        result.JobsCreated.Should().Be(2);
        var titles = await _db.Jobs.OrderBy(j => j.SalesOrderLineId).Select(j => j.Title).ToListAsync();
        titles.Should().Equal("CW-1001 x 50", "40-1700M x 250");
        var confirmed = await _db.SalesOrders.AsNoTracking().SingleAsync();
        confirmed.Status.Should().Be(SalesOrderStatus.Confirmed);
        confirmed.ConfirmedDate.Should().Be(Now);
        (await _db.ActivityLogs.AnyAsync(a => a.EntityType == "SalesOrder" && a.Action == "confirmed")).Should().BeTrue();
    }
}
