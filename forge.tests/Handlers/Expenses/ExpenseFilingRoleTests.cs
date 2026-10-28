using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Handlers.Expenses;

[Collection(CapabilityTestCollection.Name)]
public class ExpenseFilingRoleTests(CapabilityTestWebApplicationFactory factory)
{
    private const int Caller = 1;
    private const int SomeoneElse = 2;

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", Caller.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static JsonContent InvalidExpense() => JsonContent.Create(new
    {
        amount = 0,
        category = "Travel",
        description = "Mileage",
        expenseDate = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
    });

    private async Task<int> SeedExpenseFor(int userId, string description)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expense = new Expense
        {
            UserId = userId,
            Amount = 12m,
            Category = "Travel",
            Description = description,
            ExpenseDate = DateTimeOffset.UtcNow,
            Status = ExpenseStatus.Pending,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return expense.Id;
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string url)
        => (await client.GetFromJsonAsync<List<JsonElement>>(url))!;

    [Theory]
    [InlineData("ProductionWorker")]
    [InlineData("Procurement")]
    [InlineData("PM")]
    [InlineData("Controller")]
    [InlineData("Engineer")]
    [InlineData("OfficeManager")]
    public async Task Every_employee_role_passes_the_filing_role_check(string role)
    {
        var response = await Client(role).PostAsync("/api/v1/expenses", InvalidExpense());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("ProductionWorker")]
    [InlineData("Procurement")]
    public async Task Every_employee_role_passes_the_edit_role_check(string role)
    {
        var response = await Client(role).PutAsync("/api/v1/expenses/1", InvalidExpense());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("ProductionWorker")]
    [InlineData("Procurement")]
    public async Task A_filer_cannot_edit_someone_elses_expense(string role)
    {
        var otherId = await SeedExpenseFor(SomeoneElse, "someone else's taxi");

        var response = await Client(role).PutAsync($"/api/v1/expenses/{otherId}", JsonContent.Create(new
        {
            amount = 5,
            category = "Travel",
            description = "Edited",
            expenseDate = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
        }));

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Theory]
    [InlineData("ProductionWorker")]
    [InlineData("Procurement")]
    [InlineData("Engineer")]
    public async Task A_filer_lists_only_their_own_expenses(string role)
    {
        var own = await SeedExpenseFor(Caller, "own receipt");
        var other = await SeedExpenseFor(SomeoneElse, "other receipt");

        var list = await ListAsync(Client(role), "/api/v1/expenses");

        list.Select(e => e.GetProperty("id").GetInt32()).Should().Contain(own).And.NotContain(other);
        list.Should().OnlyContain(e => e.GetProperty("userId").GetInt32() == Caller);
    }

    [Fact]
    public async Task A_filer_cannot_ask_for_someone_elses_expenses()
    {
        var response = await Client("ProductionWorker").GetAsync($"/api/v1/expenses?userId={SomeoneElse}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    public async Task A_reviewer_lists_everyones_expenses(string role)
    {
        var other = await SeedExpenseFor(SomeoneElse, "reviewed receipt");

        var list = await ListAsync(Client(role), "/api/v1/expenses");

        list.Select(e => e.GetProperty("id").GetInt32()).Should().Contain(other);
    }

    [Fact]
    public async Task A_filer_cannot_read_the_activity_of_someone_elses_expense()
    {
        var other = await SeedExpenseFor(SomeoneElse, "activity receipt");
        var own = await SeedExpenseFor(Caller, "own activity receipt");

        var denied = await Client("ProductionWorker").GetAsync($"/api/v1/expenses/{other}/activity");
        var allowed = await Client("ProductionWorker").GetAsync($"/api/v1/expenses/{own}/activity");

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("ProductionWorker")]
    [InlineData("Procurement")]
    [InlineData("Engineer")]
    public async Task A_filer_still_cannot_approve_or_reimburse(string role)
    {
        var expenseId = await SeedExpenseFor(SomeoneElse, "approval receipt");

        var approve = await Client(role).PatchAsync($"/api/v1/expenses/{expenseId}/status",
            JsonContent.Create(new { status = "Approved" }));
        var reimburse = await Client(role).PostAsync($"/api/v1/expenses/{expenseId}/reimburse", null);

        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reimburse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
