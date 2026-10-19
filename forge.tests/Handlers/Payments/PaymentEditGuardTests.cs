using System.Reflection;

using FluentAssertions;
using Moq;

using Forge.Api.Concurrency;
using Forge.Api.Controllers;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Payments;

public class PaymentEditGuardTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private UpdatePaymentHandler UpdateHandler() => new(
        new PaymentRepository(_db),
        new CustomerRepository(_db),
        new Mock<ISettingsService>().Object,
        new Mock<ISystemSettingRepository>().Object,
        new Mock<IBusinessIdentifierService>().Object,
        _db);

    private async Task<Payment> SeedPaymentAsync(decimal amount = 500m)
    {
        var customer = new Customer { Name = "Acme Corp" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var payment = new Payment
        {
            PaymentNumber = "PMT-00001", CustomerId = customer.Id, Method = PaymentMethod.Check,
            Amount = amount, PaymentDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        };
        _db.Set<Payment>().Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    private async Task AddCashReceiptEntryAsync(Payment payment, JournalEntryStatus status = JournalEntryStatus.Posted)
    {
        _db.JournalEntries.Add(new JournalEntry
        {
            BookId = 1, EntryNumber = 1, EntryDate = new DateOnly(2026, 1, 15), FiscalPeriodId = 1, FiscalYearId = 1,
            Source = JournalSource.AR, SourceType = "Payment", SourceId = payment.Id, CurrencyId = 1,
            IdempotencyKey = $"AR:Payment:{payment.Id}:PAYMENT", Status = status,
        });
        await _db.SaveChangesAsync();
    }

    private static UpdatePaymentRequestModel Edit(decimal amount, string? notes = null) =>
        new("Check", amount, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), null, notes);

    [Fact]
    public async Task Update_AmountChange_WithPostedCashReceipt_IsRefused()
    {
        var payment = await SeedPaymentAsync();
        await AddCashReceiptEntryAsync(payment);

        var act = () => UpdateHandler().Handle(new UpdatePaymentCommand(payment.Id, Edit(450m)), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("general ledger");
        payment.Amount.Should().Be(500m);
    }

    [Fact]
    public async Task Update_OtherFields_WithPostedCashReceipt_AreSaved()
    {
        var payment = await SeedPaymentAsync();
        await AddCashReceiptEntryAsync(payment);

        await UpdateHandler().Handle(new UpdatePaymentCommand(payment.Id, Edit(500m, "remittance advice")), CancellationToken.None);

        payment.Notes.Should().Be("remittance advice");
    }

    [Fact]
    public async Task Update_AmountChange_WithoutPostedCashReceipt_IsSaved()
    {
        var payment = await SeedPaymentAsync();
        await AddCashReceiptEntryAsync(payment, JournalEntryStatus.Reversed);

        await UpdateHandler().Handle(new UpdatePaymentCommand(payment.Id, Edit(450m)), CancellationToken.None);

        payment.Amount.Should().Be(450m);
    }

    [Fact]
    public async Task GetById_ReturnsTheRowVersion()
    {
        var payment = await SeedPaymentAsync();
        payment.Notes = "edited";
        await _db.SaveChangesAsync();

        var result = await new GetPaymentByIdHandler(new PaymentRepository(_db))
            .Handle(new GetPaymentByIdQuery(payment.Id), CancellationToken.None);

        result.RowVersion.Should().Be(payment.Version);
        result.RowVersion.Should().BeGreaterThan(1u);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(PaymentsSettings.PolicyFull, true)]
    [InlineData(PaymentsSettings.PolicyAmendOnly, false)]
    [InlineData(PaymentsSettings.PolicyLocked, false)]
    public async Task GetById_OffersVoidOnlyUnderTheFullPolicy(string? policy, bool canVoid)
    {
        var payment = await SeedPaymentAsync();
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(PaymentsSettings.ModificationPolicyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var result = await new GetPaymentByIdHandler(new PaymentRepository(_db), settings.Object)
            .Handle(new GetPaymentByIdQuery(payment.Id), CancellationToken.None);

        result.CanVoid.Should().Be(canVoid);
    }

    [Theory]
    [InlineData(nameof(PaymentsController.UpdatePayment))]
    [InlineData(nameof(PaymentsController.DeletePayment))]
    public void Mutating_endpoints_check_IfMatch_against_the_payment_version(string action)
    {
        var attribute = typeof(PaymentsController).GetMethod(action)!.GetCustomAttribute<IfMatchAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EntityType.Should().Be(typeof(Payment));
    }
}
