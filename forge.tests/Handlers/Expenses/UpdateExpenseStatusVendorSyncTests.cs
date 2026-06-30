using System.Security.Claims;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Features.Expenses;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Expenses;

/// <summary>
/// F-26B-02 (QBO vendorless) GREEN — when an expense carrying a <see cref="Expense.VendorId"/>
/// is approved and synced to a connected provider, the enqueued <see cref="AccountingExpense"/>
/// payload must carry that vendor's <see cref="Vendor.ExternalId"/> so the purchase syncs
/// against the vendor instead of as a vendorless cash purchase. When the expense has no vendor
/// (or the vendor isn't synced), the external id stays null — unchanged behavior.
/// </summary>
public class UpdateExpenseStatusVendorSyncTests
{
    private const int ApproverUserId = 7;

    private static IHttpContextAccessor HttpContextFor(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        return accessor.Object;
    }

    /// <summary>
    /// Wires the approve handler with a connected provider and captures the JSON payload
    /// handed to the sync queue. Returns the deserialized <see cref="AccountingExpense"/>.
    /// </summary>
    private static async Task<AccountingExpense> ApproveAndCaptureSyncPayloadAsync(Expense expense)
    {
        var repo = new Mock<IExpenseRepository>();
        repo.Setup(r => r.FindAsync(expense.Id, It.IsAny<CancellationToken>())).ReturnsAsync(expense);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.GetByIdAsync(expense.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ExpenseResponseModel(
                expense.Id, expense.UserId, "Jane", null, null,
                expense.Amount, expense.Category, expense.Description, null,
                ExpenseStatus.Approved, ApproverUserId, "Jane", null,
                expense.ExpenseDate, DateTimeOffset.UtcNow));

        var provider = new Mock<IAccountingService>();
        provider.Setup(p => p.GetSyncStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountingSyncStatus(Connected: true, LastSyncAt: null, QueueDepth: 0, FailedCount: 0));

        var providerFactory = new Mock<IAccountingProviderFactory>();
        providerFactory.Setup(f => f.GetActiveProviderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider.Object);

        string? capturedPayload = null;
        var syncQueue = new Mock<ISyncQueueRepository>();
        syncQueue.Setup(q => q.EnqueueAsync("Expense", expense.Id, "CreateExpense", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, string, string?, CancellationToken>((_, _, _, payload, _) => capturedPayload = payload)
            .ReturnsAsync(new SyncQueueEntry());

        var logger = new Mock<ILogger<UpdateExpenseStatusHandler>>();

        // db null (isolated test) + no AP posting / promotion: the handler resolves the
        // vendor external id from the already-loaded Vendor nav.
        var handler = new UpdateExpenseStatusHandler(
            repo.Object, HttpContextFor(ApproverUserId), syncQueue.Object, providerFactory.Object, logger.Object);

        var command = new UpdateExpenseStatusCommand(
            expense.Id, new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, "ok"));

        await handler.Handle(command, CancellationToken.None);

        capturedPayload.Should().NotBeNull("an approved expense with a connected provider enqueues a CreateExpense sync");
        return JsonSerializer.Deserialize<AccountingExpense>(capturedPayload!)!;
    }

    [Fact] // F-26B-02 GREEN — vendor external id flows into the QBO payload
    public async Task Approving_an_expense_with_a_synced_vendor_carries_the_vendor_external_id()
    {
        var vendor = new Vendor { Id = 3001, CompanyName = "Delta", ExternalId = "QBO-VENDOR-42", IsActive = true };
        var expense = new Expense
        {
            Id = 7100, UserId = 42, Amount = 250m, Category = "Travel",
            Description = "Airfare", Status = ExpenseStatus.Pending,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            VendorId = 3001, Vendor = vendor,
        };

        var payload = await ApproveAndCaptureSyncPayloadAsync(expense);

        payload.VendorExternalId.Should().Be("QBO-VENDOR-42",
            "a vendor-settled expense must sync against its vendor, not as a vendorless cash purchase");
    }

    [Fact] // unchanged behavior — no vendor ⇒ null external id (vendorless cash purchase)
    public async Task Approving_an_expense_with_no_vendor_leaves_the_external_id_null()
    {
        var expense = new Expense
        {
            Id = 7101, UserId = 42, Amount = 99m, Category = "Meals",
            Description = "Dinner", Status = ExpenseStatus.Pending,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
        };

        var payload = await ApproveAndCaptureSyncPayloadAsync(expense);

        payload.VendorExternalId.Should().BeNull("an expense with no vendor stays a vendorless cash purchase");
    }

    [Fact] // a vendor not yet synced to the provider (no ExternalId) ⇒ null, unchanged
    public async Task Approving_an_expense_whose_vendor_is_not_synced_leaves_the_external_id_null()
    {
        var vendor = new Vendor { Id = 3002, CompanyName = "Echo", ExternalId = null, IsActive = true };
        var expense = new Expense
        {
            Id = 7102, UserId = 42, Amount = 12m, Category = "Supplies",
            Description = "Pens", Status = ExpenseStatus.Pending,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            VendorId = 3002, Vendor = vendor,
        };

        var payload = await ApproveAndCaptureSyncPayloadAsync(expense);

        payload.VendorExternalId.Should().BeNull("a vendor not yet synced to the provider has no external id to carry");
    }
}
