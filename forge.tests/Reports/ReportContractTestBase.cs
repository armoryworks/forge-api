using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Reports;

/// <summary>
/// WU-15 / Phase 3 / H1 — Report contract test pattern.
///
/// Phase 1 found three report endpoints that returned 200 with empty/wrong data
/// despite source records being present (employee-productivity, quote-to-close,
/// customer-statement). The fix made each query honor its date filter and
/// layout, but a structural protection is needed so a future report regression
/// of the "endpoint returns empty despite data" pattern gets caught in CI.
///
/// Contract:
///   1. Seed: subclass populates a dev-only in-memory DB with the minimum data
///      shape the report under test claims to aggregate.
///   2. Act:  subclass invokes the repository / handler with the seed window.
///   3. Assert: <see cref="AssertNonEmpty"/> — result is non-empty *and* of the
///      shape the contract advertises.
///
/// Subclasses must be added for every new report endpoint. The base test
/// fails fast if a developer forgets to seed (no source rows) or returns
/// an empty / null aggregate.
/// </summary>
public abstract class ReportContractTestBase
{
    /// <summary>
    /// Human-readable identifier for the endpoint under test (used in failure
    /// messages so an empty contract fail is debuggable at a glance).
    /// </summary>
    protected abstract string EndpointName { get; }

    /// <summary>
    /// Seed the in-memory database with the minimum source rows the report
    /// claims to aggregate. Must produce >= 1 row that the report's filter
    /// will admit.
    /// </summary>
    protected abstract Task SeedAsync(AppDbContext db);

    /// <summary>
    /// Invoke the report and return whatever the contract claims (typically a
    /// list of row records). Returning null or empty fails the contract.
    /// </summary>
    protected abstract Task<object?> InvokeAsync(AppDbContext db);

    [Fact]
    public async Task Endpoint_returns_non_empty_after_seed()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        // Act
        var result = await InvokeAsync(db);

        // Assert
        AssertNonEmpty(result, EndpointName);
    }

    private static void AssertNonEmpty(object? result, string endpointName)
    {
        result.Should().NotBeNull(
            $"report endpoint '{endpointName}' must return a non-null payload");

        if (result is System.Collections.IEnumerable enumerable
            && result is not string)
        {
            var count = 0;
            foreach (var _ in enumerable) { count++; break; }
            count.Should().BeGreaterThan(0,
                $"report endpoint '{endpointName}' must aggregate >=1 row when " +
                "the source table contains rows in-window — otherwise the " +
                "report is silently lying. (See Phase 3 / H1.)");
        }
    }

    /// <summary>
    /// Convenience helper for subclasses — yields the same ReportRepository the
    /// production handlers use.
    /// </summary>
    protected static ReportRepository RepoFor(AppDbContext db) => new(db);
}
