using System.Security.Claims;

using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using MediatR;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.Expenses;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Expenses;

public class CreateExpenseHandlerTests
{
    private readonly Mock<IExpenseRepository> _expenseRepo = new();
    private readonly Mock<IFileRepository> _fileRepo = new();
    private readonly Mock<IApprovalService> _approvalService = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly CreateExpenseHandler _handler;

    private readonly Faker _faker = new();
    private readonly int _userId;

    public CreateExpenseHandlerTests()
    {
        _userId = _faker.Random.Int(1, 100);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        _httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        // Permissive expense policy — no limits, no receipt required. Individual tests
        // that want to exercise policy enforcement can override this setup.
        _mediator.Setup(m => m.Send(It.IsAny<GetExpenseSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpenseSettingsResponse(
                AllowSelfApproval: false,
                AutoApproveThreshold: null,
                MaxAmount: null,
                RequireReceipt: false,
                MinDescriptionLength: 0));

        _handler = new CreateExpenseHandler(
            _expenseRepo.Object,
            _fileRepo.Object,
            _approvalService.Object,
            _mediator.Object,
            _httpContextAccessor.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesExpenseAndReturnsResponse()
    {
        // Arrange
        var amount = _faker.Finance.Amount(10, 500);
        var category = _faker.Commerce.Department();
        var description = _faker.Lorem.Sentence();
        var expenseDate = DateTime.UtcNow.AddDays(-3);
        var jobId = _faker.Random.Int(1, 50);

        var data = new CreateExpenseRequestModel(amount, category, description, jobId, null, expenseDate);

        var expectedResult = new ExpenseResponseModel(
            1, _userId, "John Doe", jobId, "JOB-0001",
            amount, category.Trim(), description.Trim(), null,
            ExpenseStatus.Pending, null, null, null, expenseDate, DateTime.UtcNow);

        _expenseRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new CreateExpenseCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Amount.Should().Be(amount);
        result.Category.Should().Be(category.Trim());

        _expenseRepo.Verify(r => r.AddAsync(It.Is<Expense>(e =>
            e.UserId == _userId &&
            e.Amount == amount &&
            e.Category == category.Trim() &&
            e.Description == description.Trim() &&
            e.JobId == jobId &&
            e.ExpenseDate == expenseDate
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SetsUserIdFromHttpContext()
    {
        // Arrange
        var data = new CreateExpenseRequestModel(
            100m, "Travel", "Business trip", null, null, DateTime.UtcNow);

        var expectedResult = new ExpenseResponseModel(
            1, _userId, "John Doe", null, null,
            100m, "Travel", "Business trip", null,
            ExpenseStatus.Pending, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

        _expenseRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new CreateExpenseCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _expenseRepo.Verify(r => r.AddAsync(It.Is<Expense>(e =>
            e.UserId == _userId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TrimsCategory()
    {
        // Arrange
        var data = new CreateExpenseRequestModel(
            50m, "  Materials  ", "  Some supplies  ", null, null, DateTime.UtcNow);

        var expectedResult = new ExpenseResponseModel(
            1, _userId, "John Doe", null, null,
            50m, "Materials", "Some supplies", null,
            ExpenseStatus.Pending, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

        _expenseRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new CreateExpenseCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _expenseRepo.Verify(r => r.AddAsync(It.Is<Expense>(e =>
            e.Category == "Materials" &&
            e.Description == "Some supplies"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithReceiptFileId_SetsOnExpense()
    {
        // Arrange
        var receiptFileId = _faker.Random.Guid().ToString();
        var data = new CreateExpenseRequestModel(
            75m, "Supplies", "Office supplies", null, receiptFileId, DateTime.UtcNow);

        var expectedResult = new ExpenseResponseModel(
            1, _userId, "John Doe", null, null,
            75m, "Supplies", "Office supplies", receiptFileId,
            ExpenseStatus.Pending, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

        _expenseRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new CreateExpenseCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _expenseRepo.Verify(r => r.AddAsync(It.Is<Expense>(e =>
            e.ReceiptFileId == receiptFileId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithoutJobId_SetsJobIdNull()
    {
        // Arrange
        var data = new CreateExpenseRequestModel(
            25m, "Meals", "Lunch meeting", null, null, DateTime.UtcNow);

        var expectedResult = new ExpenseResponseModel(
            1, _userId, "John Doe", null, null,
            25m, "Meals", "Lunch meeting", null,
            ExpenseStatus.Pending, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

        _expenseRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new CreateExpenseCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _expenseRepo.Verify(r => r.AddAsync(It.Is<Expense>(e =>
            e.JobId == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ApprovalRequestStatus.AutoApproved, 1)]
    [InlineData(ApprovalRequestStatus.Pending, 0)]
    public async Task Handle_PublishesCompletionOnlyWhenTheWorkflowAutoApproves(
        ApprovalRequestStatus requestStatus, int expectedPublishes)
    {
        _expenseRepo.Setup(r => r.AddAsync(It.IsAny<Expense>(), It.IsAny<CancellationToken>()))
            .Callback<Expense, CancellationToken>((e, _) => e.Id = 55);
        _expenseRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpenseResponseModel(
                55, _userId, "John Doe", null, null,
                18.75m, "Office Supplies", "Label tape", null,
                ExpenseStatus.Pending, null, null, null, DateTime.UtcNow, DateTime.UtcNow));
        _approvalService.Setup(a => a.SubmitForApprovalAsync(
                "Expense", 55, _userId, 18.75m, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApprovalRequest { Id = 3, EntityType = "Expense", EntityId = 55, Status = requestStatus });

        await _handler.Handle(new CreateExpenseCommand(new CreateExpenseRequestModel(
            18.75m, "Office Supplies", "Label tape", null, null, DateTime.UtcNow)), CancellationToken.None);

        _mediator.Verify(m => m.Publish(
            It.Is<ApprovalCompletedEvent>(e => e.EntityType == "Expense" && e.EntityId == 55
                && e.Approved && e.DecidedById == _userId),
            It.IsAny<CancellationToken>()), Times.Exactly(expectedPublishes));
    }

    [Fact]
    public async Task Handle_RefusesToClaimAPendingReceiptUploadedBySomeoneElse()
    {
        _fileRepo.Setup(r => r.FindAsync(77, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileAttachment { Id = 77, EntityType = "expenses", EntityId = 0, UploadedById = _userId + 1 });
        var data = new CreateExpenseRequestModel(25m, "Travel", "Parking", null, "77", DateTime.UtcNow);

        var act = () => _handler.Handle(new CreateExpenseCommand(data), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _expenseRepo.Verify(r => r.AddAsync(It.IsAny<Expense>(), It.IsAny<CancellationToken>()), Times.Never);
        _fileRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LinksTheCallersOwnPendingReceipt()
    {
        var receipt = new FileAttachment { Id = 78, EntityType = "expenses", EntityId = 0, UploadedById = _userId };
        _fileRepo.Setup(r => r.FindAsync(78, It.IsAny<CancellationToken>())).ReturnsAsync(receipt);
        _expenseRepo.Setup(r => r.AddAsync(It.IsAny<Expense>(), It.IsAny<CancellationToken>()))
            .Callback<Expense, CancellationToken>((e, _) => e.Id = 501);
        _expenseRepo.Setup(r => r.GetByIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpenseResponseModel(
                501, _userId, "John Doe", null, null, 25m, "Travel", "Parking", "78",
                ExpenseStatus.Pending, null, null, null, DateTime.UtcNow, DateTime.UtcNow));
        var data = new CreateExpenseRequestModel(25m, "Travel", "Parking", null, "78", DateTime.UtcNow);

        await _handler.Handle(new CreateExpenseCommand(data), CancellationToken.None);

        receipt.EntityId.Should().Be(501);
    }
}
