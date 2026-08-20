using FluentAssertions;
using Moq;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Payments;

public class UpdatePaymentNumberTests
{
    private readonly Mock<IPaymentRepository> _repo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<ISystemSettingRepository> _systemSettings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdatePaymentHandler _handler;

    public UpdatePaymentNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());

        _handler = new UpdatePaymentHandler(
            _repo.Object,
            _customerRepo.Object,
            _settings.Object,
            _systemSettings.Object,
            _identifiers.Object,
            _db);
    }

    private void AllowManualNumbers(bool allowed) =>
        _systemSettings.Setup(s => s.FindByKeyAsync("payments.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "payments.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoForUpdate(Payment payment)
    {
        _repo.Setup(r => r.FindWithDetailsAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private static UpdatePaymentRequestModel WithPaymentNumber(string number) =>
        new("Check", 500m, DateTime.UtcNow, null, null, PaymentNumber: number);

    [Fact]
    public async Task Renames_the_payment_number_when_manual_numbers_allowed_and_unique()
    {
        var payment = new Payment { Id = 1, PaymentNumber = "PMT-00001", CustomerId = 7, Method = PaymentMethod.Check, Amount = 500m };
        SetupRepoForUpdate(payment);
        AllowManualNumbers(true);
        _repo.Setup(r => r.PaymentNumberExistsAsync("ACME-42", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(new UpdatePaymentCommand(1, WithPaymentNumber("ACME-42")), CancellationToken.None);

        payment.PaymentNumber.Should().Be("ACME-42");
        _identifiers.Verify(i => i.IssueAsync(BusinessEntityType.Payment, 1, "PMT-00001", It.IsAny<CancellationToken>()), Times.Once);
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.Payment, 1, "ACME-42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_payment_number()
    {
        var payment = new Payment { Id = 1, PaymentNumber = "PMT-00001", CustomerId = 7, Method = PaymentMethod.Check, Amount = 500m };
        SetupRepoForUpdate(payment);
        AllowManualNumbers(true);
        _repo.Setup(r => r.PaymentNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(new UpdatePaymentCommand(1, WithPaymentNumber("TAKEN")), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        payment.PaymentNumber.Should().Be("PMT-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var payment = new Payment { Id = 1, PaymentNumber = "PMT-00001", CustomerId = 7, Method = PaymentMethod.Check, Amount = 500m };
        SetupRepoForUpdate(payment);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(new UpdatePaymentCommand(1, WithPaymentNumber("ACME-42")), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        payment.PaymentNumber.Should().Be("PMT-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_after_the_payment_has_been_applied()
    {
        // Lifecycle gate: once a payment has been applied to an invoice it is audit-committed;
        // its number must not change even with manual numbers enabled.
        var payment = new Payment { Id = 1, PaymentNumber = "PMT-00001", CustomerId = 7, Method = PaymentMethod.Check, Amount = 500m };
        payment.Applications.Add(new PaymentApplication { InvoiceId = 5, Amount = 100m });
        SetupRepoForUpdate(payment);
        AllowManualNumbers(true);
        _repo.Setup(r => r.PaymentNumberExistsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.Handle(new UpdatePaymentCommand(1, WithPaymentNumber("ACME-42")), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("before it has been applied");
        payment.PaymentNumber.Should().Be("PMT-00001");
        _identifiers.Verify(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
