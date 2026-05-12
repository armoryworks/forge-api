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
