using Microsoft.Extensions.Logging.Abstractions;
using Forge.Api.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-A — seeder idempotency + admin-state-preservation tests.
/// </summary>
public class CapabilityCatalogSeederTests
{
    [Fact]
    public async Task Running_Seeder_Twice_Is_Idempotent()
    {
        using var db = TestDbContextFactory.Create();
        var seeder = new CapabilityCatalogSeeder(db, NullLogger<CapabilityCatalogSeeder>.Instance);

        await seeder.SeedAsync();
        var count1 = db.Capabilities.Count();

        await seeder.SeedAsync();
        var count2 = db.Capabilities.Count();

        Assert.Equal(count1, count2);
        Assert.Equal(CapabilityCatalog.All.Count, count1);
    }

    [Fact]
    public async Task Seeder_Preserves_Admin_Changed_Enabled_State()
    {
        using var db = TestDbContextFactory.Create();
        var seeder = new CapabilityCatalogSeeder(db, NullLogger<CapabilityCatalogSeeder>.Instance);

        await seeder.SeedAsync();

        // Admin disables a default-on capability.
        var customer = db.Capabilities.First(c => c.Code == "CAP-MD-CUSTOMERS");
        customer.Enabled = false;
        await db.SaveChangesAsync();

        // Re-running the seeder must not flip it back on.
        await seeder.SeedAsync();
        var customerAfter = db.Capabilities.First(c => c.Code == "CAP-MD-CUSTOMERS");
        Assert.False(customerAfter.Enabled);
        Assert.True(customerAfter.IsDefaultOn);
    }

    [Fact]
    public void Catalog_Contains_Ap_Split_Codes_With_Po_Matching_Defaults()
    {
        // AP capability split (owner-ratified 2026-06): vendor bills + vendor
        // payments get dedicated codes; defaults must match CAP-P2P-PO so
        // fresh installs are behavior-neutral.
        var po = CapabilityCatalog.All.Single(c => c.Code == "CAP-P2P-PO");
        var bill = CapabilityCatalog.All.Single(c => c.Code == "CAP-P2P-BILL");
        var pay = CapabilityCatalog.All.Single(c => c.Code == "CAP-P2P-PAY");

        Assert.Equal("P2P", bill.Area);
        Assert.Equal("P2P", pay.Area);
        Assert.Equal(po.IsDefaultOn, bill.IsDefaultOn);
        Assert.Equal(po.IsDefaultOn, pay.IsDefaultOn);
        Assert.Null(bill.RequiresRoles);
        Assert.Null(pay.RequiresRoles);
    }

    [Fact]
    public async Task Seeder_Inserts_New_Catalog_Codes_On_Existing_Install_With_Default_State()
    {
        using var db = TestDbContextFactory.Create();
        var seeder = new CapabilityCatalogSeeder(db, NullLogger<CapabilityCatalogSeeder>.Instance);

        // Simulate an install seeded BEFORE the AP split codes existed.
        await seeder.SeedAsync();
        var preSplitRows = db.Capabilities
            .Where(c => c.Code == "CAP-P2P-BILL" || c.Code == "CAP-P2P-PAY")
            .ToList();
        db.Capabilities.RemoveRange(preSplitRows);
        await db.SaveChangesAsync();

        // Boot-time re-seed must insert the missing codes with Enabled = IsDefaultOn.
        await seeder.SeedAsync();

        var bill = db.Capabilities.Single(c => c.Code == "CAP-P2P-BILL");
        var pay = db.Capabilities.Single(c => c.Code == "CAP-P2P-PAY");
        var po = CapabilityCatalog.All.Single(c => c.Code == "CAP-P2P-PO");
        Assert.Equal(po.IsDefaultOn, bill.Enabled);
        Assert.Equal(po.IsDefaultOn, pay.Enabled);
        Assert.Equal(po.IsDefaultOn, bill.IsDefaultOn);
        Assert.Equal(po.IsDefaultOn, pay.IsDefaultOn);
    }

    [Fact]
    public async Task Seeder_Inserts_Catalog_Amendment_Capability_Admin()
    {
        using var db = TestDbContextFactory.Create();
        var seeder = new CapabilityCatalogSeeder(db, NullLogger<CapabilityCatalogSeeder>.Instance);

        await seeder.SeedAsync();

        var admin = db.Capabilities.FirstOrDefault(c => c.Code == "CAP-IDEN-CAPABILITY-ADMIN");
        Assert.NotNull(admin);
        Assert.True(admin!.IsDefaultOn);
        Assert.True(admin.Enabled);
        Assert.Equal("Admin", admin.RequiresRoles);
    }
}
