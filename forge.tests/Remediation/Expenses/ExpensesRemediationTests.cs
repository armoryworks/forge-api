using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Remediation.Expenses;

/// <summary>
/// Region 2 · Expenses RED tests (see ../README.md).
/// Findings: F-EXP-01 (BLOCKER, ship-gate) approval is ungated — any user approves any
/// expense; F-EXP-06 delete has no ownership check; F-26B-01 Expense has no vendor/payee
/// link; F-EXP-03 reimbursement lifecycle missing (no Reimbursed state).
/// CAP-ACCT-EXPENSES is default-on. F-EXP-02 (list owner-scope) is tracked in the catalog.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class ExpensesRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public ExpensesRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role, string userId = "1")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task<int> SeedPendingExpenseFor(int userId)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expense = new Expense
        {
            UserId = userId,
            Amount = 42m,
            Category = "Travel",
            Description = "taxi",
            ExpenseDate = DateTimeOffset.UtcNow,
            Status = ExpenseStatus.Pending,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return expense.Id;
    }

    [Fact] // F-EXP-01 GREEN — approval now requires an approver role (Admin/Manager/OfficeManager)
    public async Task A_production_worker_cannot_approve_an_expense()
    {
        var expenseId = await SeedPendingExpenseFor(userId: 2);

        var body = JsonContent.Create(new { status = "Approved" });
        var response = await AuthClient("ProductionWorker", userId: "5").PatchAsync($"/api/v1/expenses/{expenseId}/status", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "approving an expense must require an approver role — not be open to any authenticated user");
    }

    [Fact] // F-EXP-06 GREEN — delete now checks ownership (owner or approver role)
    public async Task A_non_owner_cannot_delete_someone_elses_expense()
    {
        var expenseId = await SeedPendingExpenseFor(userId: 2);

        var response = await AuthClient("Engineer", userId: "3").DeleteAsync($"/api/v1/expenses/{expenseId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "only the owner (or a manager) may delete an expense");
    }

    [Fact(Skip = "RED: F-26B-01 — Expense has no vendor/payee link, so it can't be attributed to a " +
                 "vendor anywhere. Remove Skip when Expense has a VendorId (or PayeeId) FK.")]
    public void Expense_has_a_vendor_or_payee_link()
    {
        using var db = TestDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(Expense))!;

        var link = entity.FindProperty("VendorId") ?? entity.FindProperty("PayeeId");

        link.Should().NotBeNull(
            "an expense must be attributable to a vendor/payee (for QBO bills + vendor aging)");
    }

    [Fact(Skip = "RED: F-EXP-03 — the reimbursement lifecycle is missing (no Reimbursed state). " +
                 "Remove Skip when ExpenseStatus includes Reimbursed.")]
    public void ExpenseStatus_includes_a_reimbursed_state()
    {
        Enum.GetNames(typeof(ExpenseStatus)).Should().Contain("Reimbursed",
            "approved expenses need a reimbursement terminal state to sync to AP/QBO");
    }
}
