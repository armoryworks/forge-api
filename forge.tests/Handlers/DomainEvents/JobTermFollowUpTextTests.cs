using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.Terminology;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.DomainEvents;

public class JobTermFollowUpTextTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MaterialsReady_UsesTheInstallTermAndDoesNotPrefixTheJobNumber()
    {
        using var db = TestDbContextFactory.Create();
        db.TerminologyEntries.Add(new TerminologyEntry { Key = "entity_job", Label = "Work Order" });
        var vendor = new Vendor { CompanyName = "Delta Supply" };
        var job = new Job { JobNumber = "J-4", Title = "Bracket run", AssigneeId = 7 };
        db.AddRange(vendor, job);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-1001", VendorId = vendor.Id, JobId = job.Id };
        po.Lines.Add(new PurchaseOrderLine { Description = "Bar stock", OrderedQuantity = 4m, ReceivedQuantity = 4m });
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        await new OnPurchaseOrderReceived_CheckMaterialReady(
                db, Mock.Of<IClock>(c => c.UtcNow == Due), hub.Object, NullLogger<OnPurchaseOrderReceived_CheckMaterialReady>.Instance,
                new TerminologyLabels(db))
            .Handle(new PurchaseOrderReceivedEvent(po.Id, 1, 1), CancellationToken.None);

        var task = await db.FollowUpTasks.SingleAsync();
        task.Title.Should().Be("Materials ready for Work Order J-4");
        task.Description.Should().Be("All purchase orders for Work Order J-4 have been fully received. Production can begin.");
        (await db.Notifications.SingleAsync()).Message.Should().Be("All materials for Work Order J-4 are now available.");
    }

    [Fact]
    public async Task CalendarExport_DescribesTheJobByNumberAndInstallTerm()
    {
        using var db = TestDbContextFactory.Create();
        db.TerminologyEntries.Add(new TerminologyEntry { Key = "entity_job", Label = "Work Order" });
        var track = new TrackType { Code = "production", Name = "Production", SortOrder = 1 };
        var stage = new JobStage { Code = "PROD", Name = "In Production", SortOrder = 1, TrackType = track };
        db.AddRange(track, stage);
        db.Jobs.Add(new Job { JobNumber = "J-4", Title = "Bracket run", DueDate = Due, TrackType = track, CurrentStage = stage });
        await db.SaveChangesAsync();

        var bytes = await new ExportJobsCalendarHandler(db, new TerminologyLabels(db))
            .Handle(new ExportJobsCalendarQuery(null, null), CancellationToken.None);

        var ics = Encoding.UTF8.GetString(bytes);
        ics.Should().Contain("DESCRIPTION:Work Order J-4");
        ics.Should().NotContain("Job #");
    }
}
