using Bogus;
using FluentAssertions;
using Moq;

using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class CreateInvoiceFromJobHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly AppDbContext _dbContext;
    private readonly CreateInvoiceFromJobHandler _handler;

    private readonly Faker _faker = new();

    public CreateInvoiceFromJobHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _handler = new CreateInvoiceFromJobHandler(_dbContext, _invoiceRepo.Object);
    }

    [Fact]
    public async Task Handle_CompletedJobWithCustomer_CreatesInvoice()
    {
        // Arrange
        var customerName = _faker.Company.CompanyName();
        var customer = new Customer { Name = customerName };
        _dbContext.Set<Customer>().Add(customer);
        await _dbContext.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = $"JOB-{_faker.Random.Int(1000, 9999)}",
            Title = _faker.Commerce.ProductName(),
            Description = _faker.Lorem.Sentence(),
            TrackTypeId = 1,
            CurrentStageId = 1,
            CustomerId = customer.Id,
            CompletedDate = DateTime.UtcNow.AddDays(-1),
        };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var invoiceNumber = $"INV-{_faker.Random.Int(1000, 9999)}";
        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoiceNumber);

        var command = new CreateInvoiceFromJobCommand(job.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.InvoiceNumber.Should().Be(invoiceNumber);
        result.CustomerId.Should().Be(customer.Id);
        result.CustomerName.Should().Be(customerName);
        result.Status.Should().Be(InvoiceStatus.Draft.ToString());

        _invoiceRepo.Verify(r => r.AddAsync(It.Is<Invoice>(i =>
            i.InvoiceNumber == invoiceNumber &&
            i.CustomerId == customer.Id &&
            i.Lines.Count == 1 &&
            i.Lines.First().Quantity == 1 &&
            i.Lines.First().UnitPrice == 0
        ), It.IsAny<CancellationToken>()), Times.Once);

        _invoiceRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_JobNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var command = new CreateInvoiceFromJobCommand(999);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*not found*");
    }

    [Fact]
    public async Task Handle_JobNotCompleted_ThrowsInvalidOperationException()
    {
        // Arrange
        var customer = new Customer { Name = "Test Customer" };
        _dbContext.Set<Customer>().Add(customer);
        await _dbContext.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-0001",
            Title = "Incomplete Job",
            TrackTypeId = 1,
            CurrentStageId = 1,
            CustomerId = customer.Id,
            CompletedDate = null, // Not completed
        };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var command = new CreateInvoiceFromJobCommand(job.Id);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not completed*");
    }

    [Fact]
    public async Task Handle_JobWithoutCustomer_ThrowsInvalidOperationException()
    {
        // Arrange
        var job = new Job
        {
            JobNumber = "JOB-0002",
            Title = "No Customer Job",
            TrackTypeId = 1,
            CurrentStageId = 1,
            CustomerId = null,
            CompletedDate = DateTime.UtcNow.AddDays(-1),
        };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var command = new CreateInvoiceFromJobCommand(job.Id);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*without a customer*");
    }

    [Fact]
    public async Task Handle_InvoiceLineDescriptionIncludesJobNumber()
    {
        // Arrange
        var customer = new Customer { Name = "Acme Corp" };
        _dbContext.Set<Customer>().Add(customer);
        await _dbContext.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-0042",
            Title = "Widget Assembly",
            Description = "Custom widget build",
            TrackTypeId = 1,
            CurrentStageId = 1,
            CustomerId = customer.Id,
            CompletedDate = DateTime.UtcNow.AddDays(-1),
        };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("INV-0001");

        var command = new CreateInvoiceFromJobCommand(job.Id);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _invoiceRepo.Verify(r => r.AddAsync(It.Is<Invoice>(i =>
            i.Lines.First().Description!.Contains("JOB-0042") &&
            i.Lines.First().Description!.Contains("Widget Assembly") &&
            i.Lines.First().Description!.Contains("Custom widget build")
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DueDateIs30DaysFromInvoiceDate()
    {
        // Arrange
        var customer = new Customer { Name = "Test Co" };
        _dbContext.Set<Customer>().Add(customer);
        await _dbContext.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-0003",
            Title = "Test Job",
            TrackTypeId = 1,
            CurrentStageId = 1,
            CustomerId = customer.Id,
            CompletedDate = DateTime.UtcNow.AddDays(-2),
        };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("INV-0002");

        var command = new CreateInvoiceFromJobCommand(job.Id);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _invoiceRepo.Verify(r => r.AddAsync(It.Is<Invoice>(i =>
            (i.DueDate - i.InvoiceDate).Days == 30
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotesIncludeJobNumberAndTitle()
    {
        // Arrange
        var customer = new Customer { Name = "Test Co" };
        _dbContext.Set<Customer>().Add(customer);
        await _dbContext.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-0099",
            Title = "Special Order",
            TrackTypeId = 1,
            CurrentStageId = 1,
            CustomerId = customer.Id,
            CompletedDate = DateTime.UtcNow.AddDays(-1),
        };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        _invoiceRepo.Setup(r => r.GenerateNextInvoiceNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("INV-0003");

        var command = new CreateInvoiceFromJobCommand(job.Id);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _invoiceRepo.Verify(r => r.AddAsync(It.Is<Invoice>(i =>
            i.Notes != null &&
            i.Notes.Contains("JOB-0099") &&
            i.Notes.Contains("Special Order")
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
