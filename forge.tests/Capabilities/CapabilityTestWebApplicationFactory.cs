using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;
using Serilog;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-A — WebApplicationFactory for capability tests. Replaces
/// the default JWT scheme with <see cref="TestAuthHandler"/> and seeds the
/// capability catalog into the in-memory test database.
/// </summary>
public class CapabilityTestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("MockIntegrations", "true");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test_unused");

        builder.ConfigureServices(services =>
        {
            // Strip EF Core registrations and replace with the in-memory
            // TestAppDbContext (excludes pgvector entity).
            var efDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true))
                .ToList();
            foreach (var descriptor in efDescriptors)
                services.Remove(descriptor);

            var dbName = "TestDb_Capabilities_" + Guid.NewGuid();
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            services.AddScoped<AppDbContext>(_ => new TestAppDbContext(dbOptions));

            services.AddHangfire(config => config.UseMemoryStorage());

            var healthCheckDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("HealthCheck") == true)
                .ToList();
            foreach (var descriptor in healthCheckDescriptors)
                services.Remove(descriptor);
            services.AddHealthChecks();

            // Register test auth scheme and force it as the default + challenge scheme.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
            services.PostConfigureAll<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                opts.DefaultScheme = TestAuthHandler.SchemeName;
            });
        });

        builder.UseEnvironment("Testing");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        Log.CloseAndFlush();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();
        var host = base.CreateHost(builder);

        // Phase 4 Phase-A — seed catalog + hydrate snapshot manually for the
        // in-memory DB (the production startup hook is wired against a real
        // DB and skipped under WebApplicationFactory).
        using var scope = host.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<ICapabilityCatalogSeeder>();
        seeder.SeedAsync().GetAwaiter().GetResult();
        var snapshots = host.Services.GetRequiredService<ICapabilitySnapshotProvider>();
        snapshots.RefreshAsync().GetAwaiter().GetResult();

        // Workflow Pattern Phase 3 — seed entity validators + workflow
        // definitions so integration tests share the same baseline as a
        // freshly-bootstrapped install.
        var workflowSeeder = scope.ServiceProvider.GetRequiredService<Forge.Api.Workflows.IWorkflowSubstrateSeeder>();
        workflowSeeder.SeedAsync().GetAwaiter().GetResult();

        // Units of measure — production seeds these in SeedEssentialDataAsync,
        // a hook the in-memory test host skips. The part-workflow inventory
        // gate + default-stock-UoM-at-creation logic resolve 'ea' by code, so
        // the integration tests need the base units present to mirror a
        // freshly-bootstrapped install.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.UnitsOfMeasure.Any())
        {
            db.UnitsOfMeasure.AddRange(
                new UnitOfMeasure { Code = "ea", Name = "Each", Category = UomCategory.Count, IsBaseUnit = true, DecimalPlaces = 0 },
                new UnitOfMeasure { Code = "kg", Name = "Kilogram", Category = UomCategory.Weight, IsBaseUnit = true },
                new UnitOfMeasure { Code = "mm", Name = "Millimeter", Category = UomCategory.Length });
            db.SaveChanges();
        }

        return host;
    }
}

[CollectionDefinition(Name)]
public class CapabilityTestCollection : ICollectionFixture<CapabilityTestWebApplicationFactory>
{
    public const string Name = "Capabilities";
}
