using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Forge.Api.Features.CustomerReturns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerReturns;

public class CreateCustomerReturnHandlerTests
{
    private readonly Faker _faker = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly IHttpContextAccessor _httpContext;

    public CreateCustomerReturnHandlerTests()
    {
        var claims = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1")]));
        var httpContext = new DefaultHttpContext { User = claims };
        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(x => x.HttpContext).Returns(httpContext);
        _httpContext = mock.Object;
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("J-1042");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);
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
    public async Task Handle_WithQuantity_StoresTheReturnedQuantity()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);

        var result = await handler.Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Wrong size", null, DateTime.UtcNow, false, 3.5m), CancellationToken.None);

        var saved = await db.CustomerReturns.SingleAsync(r => r.Id == result.Id);
        saved.Quantity.Should().Be(3.5m);
    }

    [Fact]
    public void Validator_ZeroQuantity_IsInvalid()
    {
        var result = new CreateCustomerReturnValidator().Validate(new CreateCustomerReturnCommand(
            1, 1, "Wrong size", null, DateTime.UtcNow, false, 0m));

        result.Errors.Should().Contain(e => e.PropertyName == "Quantity");
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesReturnWithRmaNumber()
    {
        // Arrange
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);
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

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);

        var command = new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Wrong dimensions", null, DateTime.UtcNow, true);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ReworkJobId.Should().NotBeNull();
        result.ReworkJobNumber.Should().Be("J-1042");
        result.Status.Should().Be("ReworkOrdered");

        // Verify rework job was created
        var reworkJob = await db.Jobs.FirstOrDefaultAsync(j => j.Id == result.ReworkJobId);
        reworkJob.Should().NotBeNull();
        reworkJob!.Title.Should().StartWith("[Rework]");
        reworkJob.Priority.Should().Be(JobPriority.High);
        reworkJob.CustomerId.Should().Be(customer.Id);
        reworkJob.TrackTypeId.Should().Be(job.TrackTypeId);
        reworkJob.BoardPosition.Should().Be(5);
        _identifiers.Verify(i => i.IssueAsync(BusinessEntityType.Job, reworkJob.Id, "J-1042", It.IsAny<CancellationToken>()), Times.Once);
        (await db.JobActivityLogs.AnyAsync(l => l.JobId == reworkJob.Id && l.Action == ActivityAction.Created))
            .Should().BeTrue();

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

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);
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

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);
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

        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);

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

    private static async Task<Part> SeedPartOnJobAsync(Data.Context.AppDbContext db, Job job, decimal buildQuantity)
    {
        var part = new Part { PartNumber = "FG-310", Description = "Housing", Status = PartStatus.Active, Revision = "B" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        job.PartId = part.Id;
        job.PartRevision = "B";
        job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = buildQuantity });
        await db.SaveChangesAsync();
        return part;
    }

    [Fact]
    public async Task Handle_ReworkJob_CarriesThePartAndTheReturnedQuantity()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        var part = await SeedPartOnJobAsync(db, job, 40m);
        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);

        var result = await handler.Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Cracked", null, DateTime.UtcNow, true, 3m), CancellationToken.None);

        var rework = await db.Jobs.Include(j => j.JobParts).SingleAsync(j => j.Id == result.ReworkJobId);
        rework.PartId.Should().Be(part.Id);
        rework.PartRevision.Should().Be("B");
        rework.JobParts.Should().ContainSingle(jp => jp.PartId == part.Id && jp.Quantity == 3m);
        rework.SalesOrderLineId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReworkJob_WithoutAQuantity_RebuildsTheOriginalQuantity()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        var part = await SeedPartOnJobAsync(db, job, 40m);
        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);

        var result = await handler.Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Cracked", null, DateTime.UtcNow, true), CancellationToken.None);

        var rework = await db.Jobs.Include(j => j.JobParts).SingleAsync(j => j.Id == result.ReworkJobId);
        rework.JobParts.Should().ContainSingle(jp => jp.PartId == part.Id && jp.Quantity == 40m);
    }

    [Fact]
    public async Task Handle_ReworkJob_ForAJobWithoutAPart_RecordsTheQuantityInTheDescription()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);

        var result = await handler.Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Cracked", null, DateTime.UtcNow, true, 3m), CancellationToken.None);

        var rework = await db.Jobs.Include(j => j.JobParts).SingleAsync(j => j.Id == result.ReworkJobId);
        rework.PartId.Should().BeNull();
        rework.JobParts.Should().BeEmpty();
        rework.Description.Should().Contain("Quantity returned: 3");
    }

    [Fact]
    public async Task CompletedReworkJob_IsNotOfferedForInvoicing()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        await SeedPartOnJobAsync(db, job, 40m);
        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);
        var result = await handler.Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Cracked", null, DateTime.UtcNow, true, 3m), CancellationToken.None);

        job.CompletedDate = DateTimeOffset.UtcNow.AddDays(-30);
        var rework = await db.Jobs.SingleAsync(j => j.Id == result.ReworkJobId);
        rework.CompletedDate = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var uninvoiced = await new Forge.Api.Features.Invoices.GetUninvoicedJobsHandler(db)
            .Handle(new Forge.Api.Features.Invoices.GetUninvoicedJobsQuery(), CancellationToken.None);

        uninvoiced.Should().ContainSingle(u => u.JobId == job.Id);
        uninvoiced.Should().NotContain(u => u.JobId == rework.Id);
    }

    [Fact]
    public async Task OpenReworkJob_IsNotReplenishmentSupply()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        var part = await SeedPartOnJobAsync(db, job, 40m);
        job.CompletedDate = DateTimeOffset.UtcNow.AddDays(-30);
        await db.SaveChangesAsync();
        var handler = new CreateCustomerReturnHandler(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);
        await handler.Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Cracked", null, DateTime.UtcNow, true, 3m), CancellationToken.None);

        var supply = await Forge.Api.Features.Replenishment.ReplenishmentPlanning
            .LoadOpenJobSupplyAsync(db, [part.Id], CancellationToken.None);

        supply.Should().NotContainKey(part.Id);
    }

    private CreateCustomerReturnHandler Handler(Data.Context.AppDbContext db) =>
        new(db, _mediator.Object, _httpContext, _jobRepo.Object, _identifiers.Object);

    private static async Task<SalesOrderLine> LinkToShippedLineAsync(
        Data.Context.AppDbContext db, Job job, int customerId, decimal shipped)
    {
        var order = new SalesOrder { OrderNumber = "SO-00077", CustomerId = customerId };
        var line = new SalesOrderLine { Description = "Bracket", Quantity = 10, ShippedQuantity = shipped, UnitPrice = 5, LineNumber = 1 };
        order.Lines.Add(line);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        job.SalesOrderLineId = line.Id;
        await db.SaveChangesAsync();
        return line;
    }

    [Fact]
    public async Task Handle_JobOfAnotherCustomer_IsRejected()
    {
        var (_, job, db) = await SeedTestDataAsync();
        using var _ = db;
        var other = new Customer { Name = "Other Co" };
        db.Customers.Add(other);
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(new CreateCustomerReturnCommand(
            other.Id, job.Id, "Wrong size", null, DateTime.UtcNow, false), CancellationToken.None);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        (await db.CustomerReturns.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_JobOwnedOnlyThroughItsSalesOrder_ChecksTheOrderCustomer()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        job.CustomerId = null;
        await LinkToShippedLineAsync(db, job, customer.Id, shipped: 10);
        var other = new Customer { Name = "Other Co" };
        db.Customers.Add(other);
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(new CreateCustomerReturnCommand(
            other.Id, job.Id, "Wrong size", null, DateTime.UtcNow, false), CancellationToken.None);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task Handle_JobWithNoKnownOwner_IsAccepted()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        job.CustomerId = null;
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Stock build came back", null, DateTime.UtcNow, false), CancellationToken.None);

        result.CustomerId.Should().Be(customer.Id);
    }

    [Fact]
    public async Task Handle_QuantityAboveShippedLessEarlierReturns_IsRejected()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;
        await LinkToShippedLineAsync(db, job, customer.Id, shipped: 6);

        await Handler(db).Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "First batch", null, DateTime.UtcNow, false, 4m), CancellationToken.None);

        var tooMany = () => Handler(db).Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Second batch", null, DateTime.UtcNow, false, 3m), CancellationToken.None);
        await tooMany.Should().ThrowAsync<FluentValidation.ValidationException>()
            .WithMessage("*Only 2 can be returned*");

        var rest = await Handler(db).Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Second batch", null, DateTime.UtcNow, false, 2m), CancellationToken.None);
        rest.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_QuantityOnJobWithoutSalesOrderLine_IsNotCapped()
    {
        var (customer, job, db) = await SeedTestDataAsync();
        using var _ = db;

        var result = await Handler(db).Handle(new CreateCustomerReturnCommand(
            customer.Id, job.Id, "Wrong size", null, DateTime.UtcNow, false, 500m), CancellationToken.None);

        result.Id.Should().BeGreaterThan(0);
    }
}
