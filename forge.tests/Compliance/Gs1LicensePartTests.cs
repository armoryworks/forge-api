using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Compliance;

/// <summary>
/// regulated-parts-safety C-4. A GS1 barcode license is a non-inventory "license" part with an
/// expiry + renewal lead time; the expiry-driven-renewal trigger selects parts whose expiry falls
/// within the lead window (the PO-creation + Hangfire scheduling reuse existing purchasing — follow-up).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Gs1LicensePartTests(PostgresFixture fixture)
{
    [Fact]
    public async Task License_part_round_trips_and_due_window_selects_it()
    {
        await using var db = fixture.CreateContext();

        var lic = new Part
        {
            PartNumber = $"GS1-{Guid.NewGuid():N}"[..16],
            Name = "GS1 Barcode License",
            Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Consumable,
            IsLicense = true,
            LicenseExpiresAt = DateTimeOffset.UtcNow.AddDays(20),
            LicenseRenewalLeadDays = 30,
        };
        db.Parts.Add(lic);
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateContext();
        var reloaded = await verify.Parts.SingleAsync(p => p.Id == lic.Id);
        reloaded.IsLicense.Should().BeTrue();
        reloaded.LicenseRenewalLeadDays.Should().Be(30);
        reloaded.LicenseExpiresAt.Should().NotBeNull();

        // Expiry (20d out) is inside the 30d renewal lead window → due for a renewal PO.
        var now = DateTimeOffset.UtcNow;
        var licenses = await verify.Parts.Where(p => p.IsLicense && p.LicenseExpiresAt != null).ToListAsync();
        var due = licenses.Where(p => p.LicenseExpiresAt!.Value.AddDays(-(p.LicenseRenewalLeadDays ?? 0)) <= now).ToList();
        due.Should().Contain(p => p.Id == lic.Id);
    }
}
