using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// INV-SF2 / F-033 acceptance tests for CreateMaterialIssue source-state guard.
/// Proves that archived jobs are hard-blocked from receiving material transactions.
/// </summary>
public class CreateMaterialIssueHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CreateMaterialIssueHandler _handler;

    public CreateMaterialIssueHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new CreateMaterialIssueHandler(_db);
    }

    private async Task<(Job job, Part part)> SeedAsync(bool archived = false)
    {
        var part = new Part { PartNumber = "P-MI-001", Name = "Test Part" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-MI-001",
            Description = "Test Job",
            IsArchived = archived,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        return (job, part);
    }

    // ── INV-SF2: archived-job source-state guard ──────────────────────────────

    [Fact]
    public async Task Handle_ArchivedJob_ThrowsInvalidOperation_INV_SF2()
    {
        var (job, part) = await SeedAsync(archived: true);

        var act = () => _handler.Handle(new CreateMaterialIssueCommand(
            JobId: job.Id,
            PartId: part.Id,
            OperationId: null,
            Quantity: 5m,
            BinContentId: null,
            StorageLocationId: null,
            LotNumber: null,
            IssueType: MaterialIssueType.Issue,
            Notes: null,
            IssuedById: 1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*archived job*");

        // No issue record must be written
        var issueCount = await _db.MaterialIssues.CountAsync();
        issueCount.Should().Be(0, "archived-job guard must abort before writing");
    }

    [Fact]
    public async Task Handle_ActiveJob_NoBin_CreatesIssueRecord()
    {
        var (job, part) = await SeedAsync(archived: false);

        var result = await _handler.Handle(new CreateMaterialIssueCommand(
            JobId: job.Id,
            PartId: part.Id,
            OperationId: null,
            Quantity: 3m,
            BinContentId: null,
            StorageLocationId: null,
            LotNumber: null,
            IssueType: MaterialIssueType.Issue,
            Notes: null,
            IssuedById: 1), CancellationToken.None);

        result.Should().NotBeNull();
        result.Quantity.Should().Be(3m);
        result.IssueType.Should().Be(MaterialIssueType.Issue);

        var issue = await _db.MaterialIssues.SingleAsync();
        issue.JobId.Should().Be(job.Id);
        issue.PartId.Should().Be(part.Id);
    }

    [Fact]
    public async Task Handle_NonExistentJob_ThrowsKeyNotFound()
    {
        var part = new Part { PartNumber = "P-MI-002", Name = "Test Part 2" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(new CreateMaterialIssueCommand(
            JobId: 99999,
            PartId: part.Id,
            OperationId: null,
            Quantity: 1m,
            BinContentId: null,
            StorageLocationId: null,
            LotNumber: null,
            IssueType: MaterialIssueType.Issue,
            Notes: null,
            IssuedById: 1), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Job 99999*");
    }

    public void Dispose() => _db.Dispose();
}
