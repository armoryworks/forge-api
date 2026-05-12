using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Forge.Data.Context;
using Forge.Integrations;

namespace Forge.Tests.Helpers;

/// <summary>
/// Creates an EF Core InMemory DbContext suitable for unit tests.
/// Ignores the DocumentEmbedding entity because the pgvector <c>Vector</c> type
/// is not supported by the InMemory provider.
/// </summary>
public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w =>
            {
                // Suppress the "property could not be mapped" validation error
                // raised because Vector(384) is a Postgres-only type.
                w.Ignore(InMemoryEventId.TransactionIgnoredWarning);
                w.Log(CoreEventId.ManyServiceProvidersCreatedWarning);
            })
            .Options;

        var context = new TestAppDbContext(options);
        return context;
    }
}

/// <summary>
/// Thin subclass of AppDbContext that removes the pgvector-dependent entity
/// when running against the InMemory provider (unit tests).
/// </summary>
public class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options, new SystemClock())
    {
    }

    protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // The Vector property on DocumentEmbedding is a Postgres-only type and
        // cannot be validated by the InMemory provider — exclude this entity.
        modelBuilder.Ignore<Forge.Core.Entities.DocumentEmbedding>();
    }
}
