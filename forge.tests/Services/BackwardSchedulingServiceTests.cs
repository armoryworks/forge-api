using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// When a Buy BOMEntry has a NULL per-line LeadTimeDays, the backward
/// scheduler must fall back to <see cref="IPartSourcingResolver"/> for
/// the child part's effective lead time (preferred VendorPart row) rather
/// than silently dropping the row from the maxLead computation.
/// </summary>
public class BackwardSchedulingServiceTests
{
    private static Part NewPart(string partNumber) => new()
    {
        PartNumber = partNumber,
        Name = partNumber,
        ProcurementSource = ProcurementSource.Buy,
        InventoryClass = InventoryClass.Component,
        Status = PartStatus.Active,
    };

    [Fact]
    public async Task CalculateSchedule_BomLineWithNullLeadTime_FallsBackToPreferredVendorPart()
    {
        // Arrange — parent part with two Buy BOM children. One child carries
        // a per-line LeadTimeDays = 5; the other has NULL on the line but
        // its preferred VendorPart row tracks LeadTimeDays = 21. The
        // scheduler should resolve maxLead to 21, not silently drop the
        // null row and report 5.
        using var db = TestDbContextFactory.Create();
        var parent = NewPart("PARENT");
        var childWithLine = NewPart("CHILD-LINE");
        var childFromVendorPart = NewPart("CHILD-VP");
        db.Parts.AddRange(parent, childWithLine, childFromVendorPart);
        await db.SaveChangesAsync();

        // Seed a preferred VendorPart for the second child to provide the
        // 21-day lead-time fallback.
        var vendor = new Vendor { CompanyName = "Slow Vendor" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        db.VendorParts.Add(new VendorPart
        {
            VendorId = vendor.Id,
            PartId = childFromVendorPart.Id,
            IsPreferred = true,
            LeadTimeDays = 21,
        });
        await db.SaveChangesAsync();

        db.BOMEntries.AddRange(
            new BOMEntry
            {
                ParentPartId = parent.Id,
                ChildPartId = childWithLine.Id,
                Quantity = 1,
                SourceType = BOMSourceType.Buy,
                LeadTimeDays = 5,
            },
            new BOMEntry
            {
                ParentPartId = parent.Id,
                ChildPartId = childFromVendorPart.Id,
                Quantity = 1,
                SourceType = BOMSourceType.Buy,
                LeadTimeDays = null, // ← The fall-back trigger
            });

        var customer = new Customer { Name = "ACME" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = "SO-1",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var soLine = new SalesOrderLine
        {
            SalesOrderId = so.Id,
            PartId = parent.Id,
            Quantity = 1,
            UnitPrice = 100m,
            LineNumber = 1,
        };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var clock = new SystemClock();
        var resolver = new PartSourcingResolver(db);
        var service = new BackwardSchedulingService(db, clock, resolver);

        // Act
        var schedule = await service.CalculateSchedule(soLine.Id, CancellationToken.None);

        // Assert — poOrderBy is 21 days before MaterialsNeededBy, not 5.
        var leadDays = (schedule.MaterialsNeededBy - schedule.PoOrderBy).TotalDays;
        leadDays.Should().BeApproximately(21d, precision: 0.001d);
    }
}
