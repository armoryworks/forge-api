using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class GetSalesOrderByIdLineJobTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    [Fact]
    public async Task Line_jobs_report_IsComplete_for_completed_disposed_or_archived_work_orders()
    {
        var customer = new Customer { Id = 3, Name = "Detail Co" };
        _db.Customers.Add(customer);
        _db.JobStages.Add(new JobStage { Id = 11, TrackTypeId = 1, Name = "In Production", Code = "in_production" });
        var so = new SalesOrder
        {
            Id = 70, OrderNumber = "SO-00070", CustomerId = customer.Id, Customer = customer,
            Status = SalesOrderStatus.InProduction,
        };
        var line = new SalesOrderLine { Id = 71, Description = "Widget", Quantity = 4m, UnitPrice = 1m, LineNumber = 1 };
        so.Lines.Add(line);
        _db.SalesOrders.Add(so);

        Job Work(int id, Action<Job>? shape = null)
        {
            var job = new Job
            {
                Id = id, JobNumber = $"J-{id}", Title = "Build", TrackTypeId = 1, CurrentStageId = 11,
                SalesOrderLineId = line.Id,
            };
            shape?.Invoke(job);
            return job;
        }

        _db.Jobs.AddRange(
            Work(1),
            Work(2, j => j.CompletedDate = DateTimeOffset.UnixEpoch),
            Work(3, j => j.Disposition = JobDisposition.Scrap),
            Work(4, j => j.IsArchived = true));
        await _db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var handler = new GetSalesOrderByIdHandler(new SalesOrderRepository(_db), _db, userManager.Object);

        var result = await handler.Handle(new GetSalesOrderByIdQuery(so.Id), CancellationToken.None);

        var jobs = result.Lines.Single().Jobs.ToDictionary(j => j.Id, j => j.IsComplete);
        jobs.Should().BeEquivalentTo(new Dictionary<int, bool>
        {
            [1] = false,
            [2] = true,
            [3] = true,
            [4] = true,
        });
    }
}
