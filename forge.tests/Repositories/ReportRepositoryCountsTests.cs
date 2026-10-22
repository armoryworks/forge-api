using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

public class ReportRepositoryCountsTests : IDisposable
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(-30);
    private static readonly DateTimeOffset End = DateTimeOffset.UtcNow.AddDays(1);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Shipping_summary_accounts_for_every_shipment()
    {
        var due = DateTimeOffset.UtcNow.AddDays(-5);
        _db.Customers.Add(new Customer { Id = 1, Name = "Alpha" });
        _db.SalesOrders.AddRange(
            new SalesOrder { Id = 1, OrderNumber = "SO-1", CustomerId = 1, RequestedDeliveryDate = due },
            new SalesOrder { Id = 2, OrderNumber = "SO-2", CustomerId = 1 });
        _db.Shipments.AddRange(
            new Shipment { Id = 1, ShipmentNumber = "S-1", SalesOrderId = 1, Status = ShipmentStatus.Shipped, ShippedDate = due.AddDays(-1) },
            new Shipment { Id = 2, ShipmentNumber = "S-2", SalesOrderId = 1, Status = ShipmentStatus.Shipped, ShippedDate = due.AddDays(2) },
            new Shipment { Id = 3, ShipmentNumber = "S-3", SalesOrderId = 2, Status = ShipmentStatus.Shipped, ShippedDate = due },
            new Shipment { Id = 4, ShipmentNumber = "S-4", SalesOrderId = 1, Status = ShipmentStatus.Shipped });
        await _db.SaveChangesAsync();

        var rows = await new ReportRepository(_db).GetShippingSummaryAsync(Start, End, CancellationToken.None);

        var row = rows.Should().ContainSingle().Subject;
        row.Count.Should().Be(4);
        row.OnTimeCount.Should().Be(1);
        row.LateCount.Should().Be(1);
        row.NoDueDateCount.Should().Be(1);
        row.NotShippedCount.Should().Be(1);
        (row.OnTimeCount + row.LateCount + row.NoDueDateCount + row.NotShippedCount).Should().Be(row.Count);
    }

    [Fact]
    public async Task Time_by_user_reports_exact_minutes_for_short_entries()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.TimeEntries.AddRange(
            new TimeEntry { UserId = 1, Date = today, DurationMinutes = 2 },
            new TimeEntry { UserId = 2, Date = today, DurationMinutes = 95 });
        await _db.SaveChangesAsync();

        var rows = await new ReportRepository(_db).GetTimeByUserAsync(Start, End, CancellationToken.None);

        rows.Select(r => (r.UserId, r.TotalMinutes)).Should().Equal((2, 95), (1, 2));
        rows.Single(r => r.UserId == 1).TotalHours.Should().Be(0m);
    }

    [Fact]
    public async Task Ar_aging_total_is_rounded_like_the_balance()
    {
        _db.Customers.Add(new Customer { Id = 1, Name = "Alpha" });
        var invoice = new Invoice
        {
            Id = 1,
            InvoiceNumber = "INV-1",
            CustomerId = 1,
            Status = InvoiceStatus.Sent,
            TaxRate = 0.0725m,
            InvoiceDate = DateTimeOffset.UtcNow.AddDays(-5),
            DueDate = DateTimeOffset.UtcNow.AddDays(25),
        };
        invoice.Lines.Add(new InvoiceLine { LineNumber = 1, Description = "Part", Quantity = 1, UnitPrice = 12998m });
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var rows = await new ReportRepository(_db).GetArAgingAsync(CancellationToken.None);

        var row = rows.Should().ContainSingle().Subject;
        row.Total.Should().Be(13940.36m);
        row.Total.Should().Be(row.BalanceDue);
    }
}
