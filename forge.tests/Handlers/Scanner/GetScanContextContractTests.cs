using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scanner;

public class GetScanContextContractTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private static IHttpContextAccessor Caller(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7") };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        });
        return accessor.Object;
    }

    private async Task SeedAsync()
    {
        var raw = new Part { PartNumber = "RM-100", Description = "Bar stock" };
        var assembly = new Part { PartNumber = "FG-200", Description = "Bracket" };
        _db.Parts.AddRange(raw, assembly);
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        _db.StorageLocations.Add(bin);
        var vendor = new Vendor { CompanyName = "Steel Supply" };
        _db.Vendors.Add(vendor);
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        _db.BinContents.Add(new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = raw.Id, Quantity = 40, PlacedAt = DateTimeOffset.UtcNow,
        });

        var po = new PurchaseOrder { PONumber = "PO-0007", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        _db.PurchaseOrderLines.Add(new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id, PartId = raw.Id, Description = "Bar stock", OrderedQuantity = 10, ReceivedQuantity = 4,
        });

        _db.BOMLines.Add(new BOMLine { ParentPartId = assembly.Id, ChildPartId = raw.Id, Quantity = 2 });
        var job = new Job { JobNumber = "J-1", Title = "Bracket run", TrackTypeId = 1, CurrentStageId = 1, PartId = assembly.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        _db.Set<JobPart>().Add(new JobPart { JobId = job.Id, PartId = assembly.Id, Quantity = 5 });
        _db.ScanActionLogs.Add(new ScanActionLog
        {
            UserId = 7, ActionType = ScanActionType.Issue, PartId = raw.Id, Quantity = 3,
            RelatedEntityId = job.Id, RelatedEntityType = "Job",
        });

        var order = new SalesOrder { OrderNumber = "SO-0003", CustomerId = customer.Id };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        var open = new Shipment { ShipmentNumber = "SH-0001", SalesOrderId = order.Id, Status = ShipmentStatus.Packed };
        var gone = new Shipment { ShipmentNumber = "SH-0002", SalesOrderId = order.Id, Status = ShipmentStatus.Shipped };
        _db.Shipments.AddRange(open, gone);
        await _db.SaveChangesAsync();
        _db.ShipmentLines.AddRange(
            new ShipmentLine { ShipmentId = open.Id, PartId = raw.Id, Quantity = 6 },
            new ShipmentLine { ShipmentId = gone.Id, PartId = raw.Id, Quantity = 2 });

        _db.QcChecklistTemplates.Add(new QcChecklistTemplate { Name = "Incoming", PartId = raw.Id });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task<JsonElement> ContextJsonAsync(IHttpContextAccessor caller)
    {
        var result = await new GetScanContextHandler(_db, caller)
            .Handle(new GetScanContextQuery("RM-100"), CancellationToken.None);
        return JsonSerializer.SerializeToElement(result, WebJson);
    }

    private static JsonElement Action(JsonElement context, string name) =>
        context.GetProperty("availableActions").EnumerateArray()
            .Single(a => a.GetProperty("action").GetString() == name);

    [Fact]
    public async Task ReceiveContext_IsAnObjectWithPoLinesInTheKioskShape()
    {
        await SeedAsync();

        var receive = Action(await ContextJsonAsync(Caller("ProductionWorker")), "Receive");

        receive.GetProperty("enabled").GetBoolean().Should().BeTrue();
        var line = receive.GetProperty("context").GetProperty("poLines").EnumerateArray().Single();
        line.GetProperty("purchaseOrderLineId").GetInt32().Should().BePositive();
        line.GetProperty("poNumber").GetString().Should().Be("PO-0007");
        line.GetProperty("expectedQuantity").GetDecimal().Should().Be(10);
        line.GetProperty("receivedQuantity").GetDecimal().Should().Be(4);
        line.GetProperty("remainingQuantity").GetDecimal().Should().Be(6);
    }

    [Fact]
    public async Task IssueContext_IsAnObjectWithJobsCarryingRequiredIssuedAndRemaining()
    {
        await SeedAsync();

        var issue = Action(await ContextJsonAsync(Caller("ProductionWorker")), "Issue");

        issue.GetProperty("enabled").GetBoolean().Should().BeTrue();
        var job = issue.GetProperty("context").GetProperty("jobs").EnumerateArray().Single();
        job.GetProperty("jobId").GetInt32().Should().BePositive();
        job.GetProperty("jobNumber").GetString().Should().Be("J-1");
        job.GetProperty("title").GetString().Should().Be("Bracket run");
        job.GetProperty("requiredQuantity").GetDecimal().Should().Be(10);
        job.GetProperty("issuedQuantity").GetDecimal().Should().Be(3);
        job.GetProperty("remainingQuantity").GetDecimal().Should().Be(7);
    }

    [Fact]
    public async Task IssueContext_CountsAScanIssueOnceWhenItAlsoRecordedMaterialIssues()
    {
        await SeedAsync();
        var log = await _db.ScanActionLogs.SingleAsync();
        var raw = await _db.Parts.SingleAsync(p => p.PartNumber == "RM-100");
        _db.MaterialIssues.Add(new MaterialIssue
        {
            JobId = log.RelatedEntityId!.Value, PartId = raw.Id, Quantity = 3, IssueType = MaterialIssueType.Issue,
            IssuedById = 7, IssuedAt = DateTimeOffset.UtcNow, Notes = ScanIssueNote.For(log.Id),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var issue = Action(await ContextJsonAsync(Caller("ProductionWorker")), "Issue");

        var job = issue.GetProperty("context").GetProperty("jobs").EnumerateArray().Single();
        job.GetProperty("issuedQuantity").GetDecimal().Should().Be(3);
        job.GetProperty("remainingQuantity").GetDecimal().Should().Be(7);
    }

    [Fact]
    public async Task ShipContext_ListsOnlyUnshippedLinesWithTheirShipment()
    {
        await SeedAsync();

        var ship = Action(await ContextJsonAsync(Caller("OfficeManager")), "Ship");

        ship.GetProperty("enabled").GetBoolean().Should().BeTrue();
        var line = ship.GetProperty("context").GetProperty("openShipmentLines").EnumerateArray().Single();
        line.GetProperty("shipmentLineId").GetInt32().Should().BePositive();
        line.GetProperty("shipmentId").GetInt32().Should().BePositive();
        line.GetProperty("shipmentNumber").GetString().Should().Be("SH-0001");
        line.GetProperty("salesOrderNumber").GetString().Should().Be("SO-0003");
        line.GetProperty("requiredQuantity").GetDecimal().Should().Be(6);
        line.GetProperty("requiresScanToShip").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ShipContext_FlagsALineWhoseCarrierRequiresTheLabelScan()
    {
        await SeedAsync();
        var carrier = new Carrier { Name = "Freight Co", RequiresScanToShip = true };
        _db.Carriers.Add(carrier);
        await _db.SaveChangesAsync();
        var open = await _db.Shipments.SingleAsync(s => s.ShipmentNumber == "SH-0001");
        open.CarrierId = carrier.Id;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var ship = Action(await ContextJsonAsync(Caller("OfficeManager")), "Ship");

        var line = ship.GetProperty("context").GetProperty("openShipmentLines").EnumerateArray().Single();
        line.GetProperty("requiresScanToShip").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Ship_IsDisabledForARoleThatCannotShip()
    {
        await SeedAsync();

        var ship = Action(await ContextJsonAsync(Caller("ProductionWorker")), "Ship");

        ship.GetProperty("enabled").GetBoolean().Should().BeFalse();
        ship.GetProperty("disabledReason").GetString().Should().Be("Your role cannot ship");
    }

    [Fact]
    public async Task InspectContext_CarriesTheTemplateId()
    {
        await SeedAsync();

        var inspect = Action(await ContextJsonAsync(Caller("ProductionWorker")), "Inspect");

        inspect.GetProperty("enabled").GetBoolean().Should().BeTrue();
        inspect.GetProperty("context").GetProperty("qcTemplateId").GetInt32().Should().BePositive();
    }

    [Fact]
    public async Task OnlyActionsTheKioskCanCompleteAreOffered()
    {
        await SeedAsync();

        var names = (await ContextJsonAsync(Caller("ProductionWorker"))).GetProperty("availableActions")
            .EnumerateArray().Select(a => a.GetProperty("action").GetString());

        names.Should().Equal("Move", "Count", "Receive", "Ship", "Issue", "Inspect");
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
