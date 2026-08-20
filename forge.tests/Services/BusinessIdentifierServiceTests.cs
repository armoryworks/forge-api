using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>The identifier registry: issue on create, rename with history, resolve old numbers,
/// uniqueness among active rows (a retired value frees up).</summary>
public class BusinessIdentifierServiceTests
{
    private sealed class StepClock : IClock
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow { get { _now = _now.AddSeconds(1); return _now; } }
    }

    private static BusinessIdentifierService NewService()
        => new(TestDbContextFactory.Create(), new StepClock());

    [Fact]
    public async Task Issue_creates_an_active_identifier()
    {
        var svc = NewService();
        var row = await svc.IssueAsync(BusinessEntityType.Part, 1, " PRT-1 ");
        row.Value.Should().Be("PRT-1");
        row.EffectiveTo.Should().BeNull();
        (await svc.GetCurrentAsync(BusinessEntityType.Part, 1)).Should().Be("PRT-1");
    }

    [Fact]
    public async Task Issue_is_idempotent_for_the_same_value()
    {
        var svc = NewService();
        var a = await svc.IssueAsync(BusinessEntityType.Part, 1, "PRT-1");
        var b = await svc.IssueAsync(BusinessEntityType.Part, 1, "PRT-1");
        b.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task Rename_closes_the_old_row_and_the_old_number_still_resolves()
    {
        var svc = NewService();
        await svc.IssueAsync(BusinessEntityType.Part, 1, "PRT-1");
        await svc.RenameAsync(BusinessEntityType.Part, 1, "ACME-9");

        (await svc.GetCurrentAsync(BusinessEntityType.Part, 1)).Should().Be("ACME-9");
        var old = await svc.ResolveAsync("PRT-1");
        old!.EntityId.Should().Be(1);
        old.EntityType.Should().Be(BusinessEntityType.Part);
        old.EffectiveTo.Should().NotBeNull();
        (await svc.GetHistoryAsync(BusinessEntityType.Part, 1)).Select(h => h.Value).Should().Equal("ACME-9", "PRT-1");
    }

    [Fact]
    public async Task Rename_rejects_a_value_active_on_another_entity()
    {
        var svc = NewService();
        await svc.IssueAsync(BusinessEntityType.Part, 1, "SHARED");
        await svc.IssueAsync(BusinessEntityType.Part, 2, "PRT-2");
        var act = () => svc.RenameAsync(BusinessEntityType.Part, 2, "SHARED");
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
    }

    [Fact]
    public async Task A_retired_value_frees_up_for_another_entity()
    {
        var svc = NewService();
        await svc.IssueAsync(BusinessEntityType.Part, 1, "PRT-1");
        await svc.RenameAsync(BusinessEntityType.Part, 1, "ACME-9"); // PRT-1 retired
        var reissued = await svc.IssueAsync(BusinessEntityType.Part, 2, "PRT-1");
        reissued.EntityId.Should().Be(2);
        (await svc.ResolveAsync("PRT-1"))!.EntityId.Should().Be(2); // active owner wins resolution
    }
}
