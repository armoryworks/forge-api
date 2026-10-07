using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Features.Parts;

[Collection(CapabilityTestCollection.Name)]
public class CreatePartNumberEndpointTests(CapabilityTestWebApplicationFactory factory)
{
    private const string ManualNumbersKey = "parts.allow_manual_numbers";

    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    private async Task SetManualNumbersAsync(string? value)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == ManualNumbersKey);
        if (value is null)
        {
            if (setting is not null) db.SystemSettings.Remove(setting);
        }
        else if (setting is null)
        {
            db.SystemSettings.Add(new SystemSetting { Key = ManualNumbersKey, Value = value });
        }
        else
        {
            setting.Value = value;
        }
        await db.SaveChangesAsync();
    }

    private async Task<string?> CurrentManualNumbersAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == ManualNumbersKey))?.Value;
    }

    private async Task SeedDeletedPartAsync(string partNumber)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Parts.Add(new Part
        {
            PartNumber = partNumber,
            Name = "Retired bracket",
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            DeletedAt = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string?> DetailOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("detail").GetString();
    }

    private static object Body(string partNumber) => new
    {
        name = "Bracket",
        procurementSource = "Buy",
        inventoryClass = "Component",
        partNumber,
    };

    [Fact]
    public async Task Recreating_a_deleted_part_number_returns_400_with_the_deleted_part_message()
    {
        var original = await CurrentManualNumbersAsync();
        await SetManualNumbersAsync("true");
        try
        {
            await SeedDeletedPartAsync("DEL-PN-001");

            var response = await AuthenticatedClient().PostAsJsonAsync("/api/v1/parts", Body("DEL-PN-001"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await DetailOf(response)).Should().Be(
                "Part number 'DEL-PN-001' belongs to a deleted part. Restore that part or choose another number.");
        }
        finally
        {
            await SetManualNumbersAsync(original);
        }
    }

    [Fact]
    public async Task A_typed_number_with_manual_numbers_off_returns_400_instead_of_being_replaced()
    {
        var original = await CurrentManualNumbersAsync();
        await SetManualNumbersAsync("false");
        try
        {
            var response = await AuthenticatedClient().PostAsJsonAsync("/api/v1/parts", Body("TYPED-PN-001"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await DetailOf(response)).Should().StartWith("Manual part numbers are turned off.");
        }
        finally
        {
            await SetManualNumbersAsync(original);
        }
    }
}
