using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

[Collection(PostgresCollection.Name)]
public sealed class CreateJobFromSalesOrderLinePostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_job_from_only_a_line_persists_its_part_quantity_customer_and_due_date()
    {
        var requested = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        int trackId, lineId, partId, customerId;

        await using (var seed = fixture.CreateContext())
        {
            var track = new TrackType { Name = "SO default track", Code = $"sodef-{Guid.NewGuid():N}"[..24], IsActive = true };
            seed.TrackTypes.Add(track);
            var part = new Part { PartNumber = $"SODEF-{Guid.NewGuid():N}"[..16], Description = "SO default part" };
            seed.Parts.Add(part);
            var customer = new Customer { Name = "SO default customer" };
            seed.Customers.Add(customer);
            await seed.SaveChangesAsync();

            seed.JobStages.Add(new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true });
            var so = new SalesOrder
            {
                CustomerId = customer.Id,
                OrderNumber = $"SO-{Guid.NewGuid():N}"[..16],
                Status = SalesOrderStatus.Confirmed,
                RequestedDeliveryDate = requested,
            };
            seed.SalesOrders.Add(so);
            await seed.SaveChangesAsync();

            var line = new SalesOrderLine { SalesOrderId = so.Id, PartId = part.Id, Description = "Line", Quantity = 100m, ShippedQuantity = 10m, UnitPrice = 1m, LineNumber = 1 };
            seed.SalesOrderLines.Add(line);
            await seed.SaveChangesAsync();

            var earlier = new Job
            {
                JobNumber = $"J-SODEF-{Guid.NewGuid():N}"[..16],
                Title = "Earlier",
                TrackTypeId = track.Id,
                CurrentStageId = (await seed.JobStages.SingleAsync(s => s.TrackTypeId == track.Id)).Id,
                PartId = part.Id,
                SalesOrderLineId = line.Id,
            };
            earlier.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 30m });
            seed.Jobs.Add(earlier);
            await seed.SaveChangesAsync();

            (trackId, lineId, partId, customerId) = (track.Id, line.Id, part.Id, customer.Id);
        }

        await using var db = fixture.CreateContext();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetJobByIdQuery, CancellationToken>(async (q, ct) =>
                (await new JobRepository(db, new SystemClock()).GetDetailAsync(q.Id, ct))!);

        var handler = new CreateJobHandler(
            new JobRepository(db, new SystemClock()),
            new TrackTypeRepository(db),
            mediator.Object,
            hub.Object,
            Mock.Of<IBarcodeService>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            db,
            new SalesOrderAcceptanceGate(db, StubCapabilitySnapshotProvider.Off),
            Mock.Of<ICloudFolderAutoCreator>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            StubCapabilitySnapshotProvider.Off);

        var result = await handler.Handle(
            new CreateJobCommand("From the line", null, trackId, null, null, null, null, SalesOrderLineId: lineId),
            CancellationToken.None);

        result.PartId.Should().Be(partId);
        result.CustomerId.Should().Be(customerId);
        result.DueDate.Should().Be(requested);

        await using var verify = fixture.CreateContext();
        var jobPart = await verify.JobParts.SingleAsync(jp => jp.JobId == result.Id);
        jobPart.PartId.Should().Be(partId);
        jobPart.Quantity.Should().Be(60m);

        var lines = await new GetAssignableSalesOrderLinesHandler(
                verify, new SalesOrderAcceptanceGate(verify, StubCapabilitySnapshotProvider.Off))
            .Handle(new GetAssignableSalesOrderLinesQuery(true, null), CancellationToken.None);
        var row = lines.Single(l => l.Id == lineId);
        row.RemainingQuantity.Should().Be(0m);
        row.RequestedDeliveryDate.Should().Be(requested);
    }
}
