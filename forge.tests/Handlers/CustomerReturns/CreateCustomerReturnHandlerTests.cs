using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Forge.Api.Features.CustomerReturns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerReturns;

public class CreateCustomerReturnHandlerTests
{
    private readonly Faker _faker = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly IHttpContextAccessor _httpContext;

    public CreateCustomerReturnHandlerTests()
    {
        var claims = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1")]));
        var httpContext = new DefaultHttpContext { User = claims };
        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(x => x.HttpContext).Returns(httpContext);
        _httpContext = mock.Object;
    }

    private async Task<(Customer customer, Job job, Data.Context.AppDbContext db)> SeedTestDataAsync()
    {
        var db = TestDbContextFactory.Create();

        var customer = new Customer { Name = _faker.Company.CompanyName() };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var trackType = new TrackType { Name = "Production" };
        db.TrackTypes.Add(trackType);
        await db.SaveChangesAsync();

        var stage = new JobStage
        {
            Name = "Quote",
            TrackTypeId = trackType.Id,
            SortOrder = 1,
        };
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

        return (customer, job, db);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesReturnWithRmaNumber()
    {
        // Arrange
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext);
        var returnDate = DateTime.UtcNow;

        var command = new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Defective part", "Crack in housing", returnDate, false);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ReturnNumber.Should().StartWith("RMA-");
        result.CustomerId.Should().Be(customer.Id);
        result.CustomerName.Should().Be(customer.Name);
        result.OriginalJobId.Should().Be(job.Id);
        result.OriginalJobNumber.Should().Be("JOB-00001");
        result.Status.Should().Be("Received");
        result.Reason.Should().Be("Defective part");
        result.ReworkJobId.Should().BeNull();
        result.ReworkJobNumber.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithCreateReworkJob_CreatesLinkedReworkJob()
    {
        // Arrange
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext);

        var command = new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Wrong dimensions", null, DateTime.UtcNow, true);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ReworkJobId.Should().NotBeNull();
        result.ReworkJobNumber.Should().NotBeNull();
        result.Status.Should().Be("ReworkOrdered");

        // Verify rework job was created
        var reworkJob = await db.Jobs.FirstOrDefaultAsync(j => j.Id == result.ReworkJobId);
        reworkJob.Should().NotBeNull();
        reworkJob!.Title.Should().StartWith("[Rework]");
        reworkJob.Priority.Should().Be(JobPriority.High);
        reworkJob.CustomerId.Should().Be(customer.Id);
        reworkJob.TrackTypeId.Should().Be(job.TrackTypeId);

        // Verify job link was created
        var link = await db.JobLinks.FirstOrDefaultAsync(l =>
            l.SourceJobId == job.Id && l.TargetJobId == result.ReworkJobId);
        link.Should().NotBeNull();
        link!.LinkType.Should().Be(JobLinkType.RelatedTo);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var (_, job, db) = await SeedTestDataAsync();
        using var _ = db;

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext);
        var nonExistentCustomerId = 9999;

        var command = new CreateCustomerReturnCommand(
            nonExistentCustomerId, job.Id, "Reason", null, DateTime.UtcNow, false);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Customer {nonExistentCustomerId}*");
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var (customer, _, db) = await SeedTestDataAsync();
        using var _ = db;

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext);
        var nonExistentJobId = 9999;

        var command = new CreateCustomerReturnCommand(
            customer.Id, nonExistentJobId, "Reason", null, DateTime.UtcNow, false);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Job {nonExistentJobId}*");
    }

    [Fact]
    public async Task Handle_SequentialReturns_IncrementsRmaNumber()
    {
        // Arrange
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext);

        // Create first return
        var command1 = new CreateCustomerReturnCommand(
            customer.Id, job.Id, "First return", null, DateTime.UtcNow, false);
        var result1 = await handler.Handle(command1, CancellationToken.None);

        // Create second return
        var command2 = new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Second return", null, DateTime.UtcNow, false);
        var result2 = await handler.Handle(command2, CancellationToken.None);

        // Assert
        result1.ReturnNumber.Should().Be("RMA-00001");
        result2.ReturnNumber.Should().Be("RMA-00002");
    }
}
