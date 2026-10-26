using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Handlers.Expenses;

[Collection(CapabilityTestCollection.Name)]
public class ExpenseFilingRoleTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
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

    [Fact]
    public async Task A_production_worker_cannot_file_an_expense()
    {
        var response = await Client("ProductionWorker").PostAsync("/api/v1/expenses", InvalidExpense());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_production_worker_cannot_edit_an_expense()
    {
        var response = await Client("ProductionWorker").PutAsync("/api/v1/expenses/1", InvalidExpense());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Engineer")]
    [InlineData("OfficeManager")]
    public async Task Filing_roles_pass_the_role_check(string role)
    {
        var response = await Client(role).PostAsync("/api/v1/expenses", InvalidExpense());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
