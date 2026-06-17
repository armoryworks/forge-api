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
/// <para>
/// <b>External-Postgres override:</b> if the <c>FORGE_TEST_PG</c> environment
/// variable holds a connection string, the fixture connects to that instead of
/// starting a container. This is for environments where the Testcontainers
/// <c>Docker.DotNet</c> client cannot reach the daemon socket (e.g. a sandbox that
/// proxies the docker CLI but blocks raw-socket access, or a uid not in the
/// <c>docker</c> group) — point it at a manually-started <c>pgvector/pgvector:pg17</c>
/// container: <c>FORGE_TEST_PG="Host=localhost;Port=55432;Database=forge_test;Username=forge;Password=forgetest"</c>.
/// Unset (the default) → Testcontainers, exactly as before.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // When FORGE_TEST_PG is set we connect to that external Postgres and skip the
    // container entirely (Testcontainers' Docker.DotNet client is unavailable).
    private readonly string? _externalConnectionString =
        Environment.GetEnvironmentVariable("FORGE_TEST_PG");

    private readonly PostgreSqlContainer? _container;

    public PostgresFixture()
    {
        if (string.IsNullOrWhiteSpace(_externalConnectionString))
        {
            _container = new PostgreSqlBuilder()
                // pgvector image — the migration history calls CREATE EXTENSION vector.
                .WithImage("pgvector/pgvector:pg17")
                .Build();
        }
    }

    public string ConnectionString =>
        _externalConnectionString ?? _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (_container is not null)
            await _container.StartAsync();

        // Schema is created from the forge-db declarative schema (the same path the app boots),
        // not EF migrations — EF migrations were retired in the db cutover (2026-06-17). This
        // applies the full schema including the pgvector extension + acct_journal immutability
        // triggers, which the ledger-trigger/GL-atomicity tests in this collection rely on.
        await using var ctx = CreateContext();
        await Forge.Data.SchemaBootstrapper.EnsureSchemaAsync(ctx);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

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
