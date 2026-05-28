using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Testcontainers.PostgreSql;

using Forge.Data.Context;
using Forge.Integrations;

namespace Forge.Tests.Helpers;

/// <summary>
/// Spins up a real PostgreSQL (with the pgvector extension) in a throwaway
/// container and applies the full migration history once per test collection.
/// <para>
/// Required for the set-default findings (BE-1 / F-12-BE-01 / F-12-BE-02 /
/// F-14-BE-02): the bug is a <b>filtered unique index</b> (<c>is_default = true</c>)
/// race that the EF Core InMemory provider cannot model — InMemory enforces no
/// such constraint and cannot run <c>ExecuteUpdate</c>. Only a real Postgres
/// reproduces the violation and proves the atomic-swap fix.
/// </para>
/// Uses the real <see cref="AppDbContext"/> (not the InMemory test subclass) so
/// the pgvector <c>DocumentEmbedding</c> column migrates cleanly.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        // pgvector image — the migration history calls CREATE EXTENSION vector.
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Fresh real <see cref="AppDbContext"/> bound to the container.</summary>
    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, new SystemClock());
    }
}

[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
