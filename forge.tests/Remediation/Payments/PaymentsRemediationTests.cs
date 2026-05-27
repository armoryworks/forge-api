using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Payments;

/// <summary>
/// Region 2 · Payments RED tests (see ../README.md). Finding P06-5: payments were
/// delete-only — no amend, no void/reversal, so a corrected payment left no audit trail.
/// Now GREEN: amend (PUT) + void (POST .../void), both gated by the admin-selectable
/// payments.modification-policy (Locked / AmendOnly / Full). CAP-O2C-CASH is default-on.
/// Void is migration-free: it reverses the payment's applications, recomputes the affected
/// invoices, soft-deletes the payment, and logs the reason (lossless).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PaymentsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public PaymentsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task<int> SeedPayment(decimal amount = 100m)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { Name = "P06-5 Customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            PaymentNumber = $"PMT-{Guid.NewGuid().ToString("N")[..8]}",
            CustomerId = customer.Id,
            Method = PaymentMethod.Check,
            Amount = amount,
            PaymentDate = DateTimeOffset.UtcNow,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment.Id;
    }

    [Fact] // P06-5 GREEN — a recorded payment can be amended (policy: full)
    public async Task Payment_can_be_amended()
    {
        var paymentId = await SeedPayment();

        var body = JsonContent.Create(new
        {
            method = "Check",
            amount = 150m,
            paymentDate = DateTimeOffset.UtcNow,
            referenceNumber = "CHK-REAMENDED",
            notes = "corrected amount",
        });
        var response = await AuthClient().PutAsync($"/api/v1/payments/{paymentId}", body);

        response.IsSuccessStatusCode.Should().BeTrue("a recorded payment must be amendable under the default policy");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("CHK-REAMENDED", "the amendment must persist");
    }

    [Fact] // P06-5 GREEN — the settings-selectable policy can lock modifications
    public async Task Amending_is_rejected_when_the_policy_is_locked()
    {
        var paymentId = await SeedPayment();
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SystemSettings.Add(new SystemSetting { Key = "payments.modification-policy", Value = "locked" });
            await db.SaveChangesAsync();
        }

        var body = JsonContent.Create(new
        {
            method = "Check",
            amount = 150m,
            paymentDate = DateTimeOffset.UtcNow,
            referenceNumber = "CHK-X",
            notes = (string?)null,
        });
        var response = await AuthClient().PutAsync($"/api/v1/payments/{paymentId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the 'locked' payment policy must block amendments");
    }

    [Fact] // P06-5 GREEN — a recorded payment can be voided (with a reason)
    public async Task Payment_can_be_voided()
    {
        var paymentId = await SeedPayment();

        var body = JsonContent.Create(new { reason = "duplicate entry" });
        var response = await AuthClient().PostAsync($"/api/v1/payments/{paymentId}/void", body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "voiding a payment must reverse + remove it (audit-logged), not just hard-delete");
    }
}
