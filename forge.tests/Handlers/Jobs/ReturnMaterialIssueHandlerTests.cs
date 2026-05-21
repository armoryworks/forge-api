using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// INV-SF2 / F-033 — ReturnMaterialIssue archived-job guard.
/// Mirrors CreateMaterialIssueHandlerTests: the same guard applies to returns.
/// Gap identified in post-commit audit (ReturnMaterialIssue was guarded in 5f6bef1
/// but had no corresponding test in that commit).
/// </summary>
public class ReturnMaterialIssueHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ReturnMaterialIssueHandler _handler;

    public ReturnMaterialIssueHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new ReturnMaterialIssueHandler(_db);
    }

    private async Task<(Job job, Part part, MaterialIssue issue)> SeedAsync(bool archived = false)
    {
        var part = new Part { PartNumber = "P-RET-001", Name = "Return Part" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-RET-001",
            Description = "Return Test Job",
            IsArchived = archived,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        // Seed an original Issue record to return against
        var issue = new MaterialIssue
        {
            JobId = job.Id,
            PartId = part.Id,
            Quantity = 5m,
            UnitCost = 10m,
            IssueType = MaterialIssueType.Issue,
            IssuedById = 1,
            IssuedAt = DateTimeOffset.UtcNow,
        };
        _db.MaterialIssues.Add(issue);
        await _db.SaveChangesAsync();

        return (job, part, issue);
    }

    // ── INV-SF2: archived-job guard on return path ────────────────────────────

    [Fact]
    public async Task Handle_ArchivedJob_ThrowsInvalidOperation_INV_SF2()
    {
        var (job, _, issue) = await SeedAsync(archived: true);

        var act = () => _handler.Handle(
            new ReturnMaterialIssueCommand(job.Id, issue.Id, ReturnedById: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*archived job*");

        // No return record must be written
        var returnCount = await _db.MaterialIssues
            .CountAsync(m => m.IssueType == MaterialIssueType.Return);
        returnCount.Should().Be(0, "archived-job guard must abort before writing the return record");
    }

    // ── happy path: active job return succeeds ────────────────────────────────

    [Fact]
    public async Task Handle_ActiveJob_CreatesReturnRecord()
    {
        var (job, part, issue) = await SeedAsync(archived: false);

        var result = await _handler.Handle(
            new ReturnMaterialIssueCommand(job.Id, issue.Id, ReturnedById: 1),
            CancellationToken.None);

        result.Should().NotBeNull();
        result.IssueType.Should().Be(MaterialIssueType.Return);
        result.Quantity.Should().Be(issue.Quantity);

        var returnRecord = await _db.MaterialIssues
            .SingleAsync(m => m.IssueType == MaterialIssueType.Return);
        returnRecord.JobId.Should().Be(job.Id);
        returnRecord.PartId.Should().Be(part.Id);
    }

    // ── guard does not apply when issue is missing ────────────────────────────

    [Fact]
    public async Task Handle_NonExistentIssue_ThrowsKeyNotFound()
    {
        var (job, _, _) = await SeedAsync(archived: false);

        var act = () => _handler.Handle(
            new ReturnMaterialIssueCommand(job.Id, IssueId: 99999, ReturnedById: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    public void Dispose() => _db.Dispose();
}
