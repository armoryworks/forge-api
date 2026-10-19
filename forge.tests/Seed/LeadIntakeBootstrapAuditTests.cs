using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Data;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Seed;

/// <summary>
/// The first-boot lead-intake key is issued while the boot scope has audit capture suppressed,
/// so the bootstrap writes its own audit row. The row carries the key prefix and owner only,
/// never the hash.
/// </summary>
public class LeadIntakeBootstrapAuditTests
{
    [Fact]
    public async Task Bootstrap_key_gets_an_audit_row_without_the_hash()
    {
        await using var db = TestDbContextFactory.Create();
        db.SuppressAudit = true;

        var services = new ServiceCollection();
        services.AddSingleton<AppDbContext>(db);
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddSingleton<IClock>(new SystemClock());
        services.AddScoped<ISystemAuditWriter, SystemAuditWriter>();
        services.AddLogging();
        await using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>()
            .CreateAsync(new IdentityRole<int>(SeedData.LeadIntakeRoleName));

        await SeedData.SeedLeadIntakeBootstrapAsync(scope.ServiceProvider);

        var key = await db.SystemApiKeys.SingleAsync();
        var audit = await db.AuditLogEntries.SingleAsync();
        audit.Action.Should().Be("SystemApiKeyCreated");
        audit.UserId.Should().Be(0);
        audit.EntityType.Should().Be(nameof(SystemApiKey));
        audit.EntityId.Should().Be(key.Id);
        audit.Details.Should().Contain(key.KeyPrefix).And.NotContain(key.KeyHash);
    }
}
