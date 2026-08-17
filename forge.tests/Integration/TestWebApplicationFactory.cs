using Hangfire;
using Hangfire.MemoryStorage;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Forge.Data.Context;
using Forge.Tests.Helpers;

using Serilog;

namespace Forge.Tests.Integration;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("MockIntegrations", "true");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test_unused");
        // F-053: appsettings no longer ships a Jwt:Key; supply a test key so the app boots.
        builder.UseSetting("Jwt:Key", "integration-test-jwt-signing-key-with-32plus-chars!!");

        builder.ConfigureServices(services =>
        {
            // Remove all EF Core DbContext registrations to avoid dual-provider conflict
            var efDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true))
                .ToList();
            foreach (var descriptor in efDescriptors)
                services.Remove(descriptor);

            // Add in-memory database using TestAppDbContext (excludes pgvector DocumentEmbedding entity)
            var dbName = "TestDb_" + Guid.NewGuid();
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            services.AddScoped<AppDbContext>(_ => new TestAppDbContext(dbOptions));

            // Replace Hangfire PostgreSQL storage with in-memory
            services.AddHangfire(config => config.UseMemoryStorage());

            // Remove health checks that depend on external services
            var healthCheckDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("HealthCheck") == true)
                .ToList();
            foreach (var descriptor in healthCheckDescriptors)
                services.Remove(descriptor);

            services.AddHealthChecks();
        });

        builder.UseEnvironment("Testing");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Reset Serilog to avoid "logger is already frozen" when factory is recreated
        Log.CloseAndFlush();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();

        var host = base.CreateHost(builder);

        // Seed the capability catalog + hydrate the snapshot, exactly as
        // CapabilityTestWebApplicationFactory does: the production startup hook is
        // skipped under WebApplicationFactory, which left this host with an EMPTY
        // snapshot — every [RequiresCapability] endpoint answered 403. That was
        // invisible while every gated controller also carried [Authorize] (401
        // wins first); it surfaced the moment an [AllowAnonymous]+kiosk-auth
        // controller (shop-floor) gained its gate. Default-ON capabilities are now
        // enabled here, matching a freshly bootstrapped install.
        using (var scope = host.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Forge.Api.Capabilities.ICapabilityCatalogSeeder>();
            seeder.SeedAsync().GetAwaiter().GetResult();
        }
        host.Services.GetRequiredService<Forge.Api.Capabilities.ICapabilitySnapshotProvider>()
            .RefreshAsync().GetAwaiter().GetResult();

        return host;
    }
}

/// <summary>
/// Collection definition so all integration test classes share a single factory instance.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestWebApplicationFactory>
{
    public const string Name = "Integration";
}
