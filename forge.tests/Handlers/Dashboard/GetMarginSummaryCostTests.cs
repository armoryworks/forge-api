using FluentAssertions;
using Moq;

using Forge.Api.Features.Dashboard;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Dashboard;

public class GetMarginSummaryCostTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public GetMarginSummaryCostTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(_now);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Job> SeedInvoicedJobAsync(int id, decimal invoicedAmount, InvoiceStatus status = InvoiceStatus.Sent)
    {
        var order = new SalesOrder { Id = id, OrderNumber = $"SO-{id}", CustomerId = 1 };
        var line = new SalesOrderLine { Id = id, SalesOrder = order, LineNumber = 1, Description = "Bracket", Quantity = 1, UnitPrice = invoicedAmount };
        var invoice = new Invoice
        {
            Id = id,
            InvoiceNumber = $"INV-{id}",
            CustomerId = 1,
            SalesOrder = order,
            Status = status,
            InvoiceDate = _now,
            DueDate = _now.AddDays(30),
            Lines = { new InvoiceLine { LineNumber = 1, Description = "Bracket", Quantity = 1, UnitPrice = invoicedAmount } },
        };
        var job = new Job
        {
            Id = id,
            Title = $"Job {id}",
            JobNumber = $"JOB-{id}",
            TrackTypeId = 1,
            CurrentStageId = 1,
            SalesOrderLine = line,
            CreatedAt = _now,
        };

        _db.SalesOrders.Add(order);
        _db.SalesOrderLines.Add(line);
        _db.Invoices.Add(invoice);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private Task<Forge.Core.Models.MarginSummaryResponseModel> RunAsync() =>
        new GetMarginSummaryHandler(_db, _clock.Object).Handle(new GetMarginSummaryQuery(), CancellationToken.None);

    [Fact]
    public async Task Handle_JobWithNoRecordedCost_IsNotReportedAsFullMargin()
    {
        await SeedInvoicedJobAsync(1, 1000m);

        var result = await RunAsync();

        result.JobCount.Should().Be(1);
        result.CostedJobCount.Should().Be(0);
        result.AverageMarginPercentage.Should().Be(0m);
        result.TotalRevenue.Should().Be(1000m);
    }

    [Fact]
    public async Task Handle_AveragesMarginOnlyOverJobsWithRecordedCost()
    {
        var costed = await SeedInvoicedJobAsync(1, 1000m);
        await SeedInvoicedJobAsync(2, 500m);
        _db.Expenses.Add(new Expense { JobId = costed.Id, UserId = 1, Amount = 750m, Category = "Material", Description = "Bar stock" });
        await _db.SaveChangesAsync();

        var result = await RunAsync();

        result.JobCount.Should().Be(2);
        result.CostedJobCount.Should().Be(1);
        result.AverageMarginPercentage.Should().Be(25.0m);
        result.TotalCost.Should().Be(750m);
    }

    [Fact]
    public async Task Handle_NoJobsInWindow_ReportsNoCostedJobs()
    {
        var result = await RunAsync();

        result.JobCount.Should().Be(0);
        result.CostedJobCount.Should().Be(0);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Voided)]
    public async Task Handle_DraftAndVoidedInvoices_AreNotRevenue(InvoiceStatus status)
    {
        await SeedInvoicedJobAsync(1, 1000m, status);

        var result = await RunAsync();

        result.TotalRevenue.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_InvoiceLinesForOtherOrderLines_AreNotCreditedToTheJob()
    {
        var job = await SeedInvoicedJobAsync(1, 1000m);
        var invoice = _db.Invoices.Single(i => i.Id == 1);
        invoice.Lines.Add(new InvoiceLine { LineNumber = 2, Description = "Freight", Quantity = 1, UnitPrice = 400m });
        _db.Expenses.Add(new Expense { JobId = job.Id, UserId = 1, Amount = 500m, Category = "Material", Description = "Bar stock" });
        await _db.SaveChangesAsync();

        var result = await RunAsync();

        result.TotalRevenue.Should().Be(1000m);
        result.AverageMarginPercentage.Should().Be(50.0m);
    }

    [Fact]
    public async Task Handle_AverageMarginIsWeightedByRevenue()
    {
        var large = await SeedInvoicedJobAsync(1, 9000m);
        var small = await SeedInvoicedJobAsync(2, 1000m);
        _db.Expenses.Add(new Expense { JobId = large.Id, UserId = 1, Amount = 8100m, Category = "Material", Description = "Bar stock" });
        _db.Expenses.Add(new Expense { JobId = small.Id, UserId = 1, Amount = 1m, Category = "Material", Description = "Shim" });
        await _db.SaveChangesAsync();

        var result = await RunAsync();

        result.CostedJobCount.Should().Be(2);
        result.AverageMarginPercentage.Should().Be(19.0m);
    }
}
