using Bogus;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.CustomerReturns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerReturns;

public class ResolveCustomerReturnHandlerTests
{
    private readonly Faker _faker = new();

    private async Task<(CustomerReturn ret, Data.Context.AppDbContext db)> SeedReturnAsync(
        CustomerReturnStatus status = CustomerReturnStatus.Received)
    {
        var db = TestDbContextFactory.Create();

        var customer = new Customer { Name = _faker.Company.CompanyName() };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var trackType = new TrackType { Name = "Production" };
        db.TrackTypes.Add(trackType);
        await db.SaveChangesAsync();

        var stage = new JobStage { Name = "Quote", TrackTypeId = trackType.Id, SortOrder = 1 };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-00001",
            Title = "Original Job",
            TrackTypeId = trackType.Id,
            CurrentStageId = stage.Id,
            CustomerId = customer.Id,
            Priority = JobPriority.Normal,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var ret = new CustomerReturn
        {
            ReturnNumber = "RMA-00001",
            CustomerId = customer.Id,
            OriginalJobId = job.Id,
            Reason = "Defective part",
            ReturnDate = DateTime.UtcNow,
            Status = status,
        };
        db.CustomerReturns.Add(ret);
        await db.SaveChangesAsync();

        return (ret, db);
    }

    [Fact]
    public async Task Handle_WithInspectionNotes_PersistsNotesAndResolves()
    {
        // Arrange
        var (ret, db) = await SeedReturnAsync();
        using var _ = db;

        var handler = new ResolveCustomerReturnHandler(db);
        var command = new ResolveCustomerReturnCommand(ret.Id, "Inspected: crack confirmed, scrapped");

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var updated = await db.CustomerReturns.FirstAsync(r => r.Id == ret.Id);
        updated.Status.Should().Be(CustomerReturnStatus.Resolved);
        updated.InspectionNotes.Should().Be("Inspected: crack confirmed, scrapped");

        var activity = await db.ActivityLogs
            .FirstOrDefaultAsync(a => a.EntityType == "CustomerReturn" && a.EntityId == ret.Id && a.Action == "resolved");
        activity.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WithoutInspectionNotes_LeavesExistingNotesUntouched()
    {
        // Arrange
        var (ret, db) = await SeedReturnAsync();
        using var _ = db;
        ret.InspectionNotes = "Pre-existing notes";
        await db.SaveChangesAsync();

        var handler = new ResolveCustomerReturnHandler(db);
        var command = new ResolveCustomerReturnCommand(ret.Id, null);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var updated = await db.CustomerReturns.FirstAsync(r => r.Id == ret.Id);
        updated.Status.Should().Be(CustomerReturnStatus.Resolved);
        updated.InspectionNotes.Should().Be("Pre-existing notes");
    }

    [Fact]
    public async Task Handle_ClosedReturn_ThrowsInvalidOperationException()
    {
        // Arrange
        var (ret, db) = await SeedReturnAsync(CustomerReturnStatus.Closed);
        using var _ = db;

        var handler = new ResolveCustomerReturnHandler(db);
        var command = new ResolveCustomerReturnCommand(ret.Id, "notes");

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*closed*");
    }

    [Fact]
    public async Task Handle_ReturnNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var (_, db) = await SeedReturnAsync();
        using var _ = db;

        var handler = new ResolveCustomerReturnHandler(db);
        var command = new ResolveCustomerReturnCommand(9999, null);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Customer return 9999*");
    }
}
