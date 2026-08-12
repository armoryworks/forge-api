using FluentAssertions;

using Forge.Api.Features.Leads;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leads;

/// <summary>
/// Drives <see cref="GetLeadsHandler"/> through the real <see cref="LeadRepository"/>
/// (InMemory context) — the externalId exact-match filter lives in the repository
/// query, so a mocked repo would prove nothing. externalId is the intake relays'
/// "did I already create this lead?" dedupe probe (docs/api-key-integrations.md §1.6).
/// </summary>
public class GetLeadsHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetLeadsHandler _handler;

    public GetLeadsHandlerTests()
    {
        _handler = new GetLeadsHandler(new LeadRepository(_db));
    }

    private async Task SeedLeadsAsync()
    {
        _db.Leads.AddRange(
            new Lead { Id = 1, CompanyName = "Acme Corp", ExternalId = "tuyere:sub-1", CreatedBy = 1 },
            new Lead { Id = 2, CompanyName = "Globex", ExternalId = "tuyere:sub-2", CreatedBy = 1 },
            new Lead { Id = 3, CompanyName = "Initech", CreatedBy = 1 });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_ExternalIdFilter_ReturnsExactMatchOnly()
    {
        await SeedLeadsAsync();

        var result = await _handler.Handle(
            new GetLeadsQuery(null, null, "tuyere:sub-2"), CancellationToken.None);

        result.Should().ContainSingle(
            "the dedupe probe must never return unrelated leads — that's the bug this filter fixes");
        result[0].Id.Should().Be(2);
        result[0].ExternalId.Should().Be("tuyere:sub-2");
    }

    [Fact]
    public async Task Handle_ExternalIdFilter_TrimsBeforeMatching()
    {
        await SeedLeadsAsync();

        var result = await _handler.Handle(
            new GetLeadsQuery(null, null, "  tuyere:sub-1  "), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ExternalIdFilter_NoMatch_ReturnsEmpty()
    {
        await SeedLeadsAsync();

        var result = await _handler.Handle(
            new GetLeadsQuery(null, null, "tuyere:sub-999"), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoExternalId_ReturnsAllLeads()
    {
        await SeedLeadsAsync();

        var result = await _handler.Handle(
            new GetLeadsQuery(null, null), CancellationToken.None);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_ExternalIdFilter_ComposesWithStatusFilter()
    {
        _db.Leads.AddRange(
            new Lead { Id = 1, CompanyName = "Acme Corp", ExternalId = "tuyere:sub-1", Status = LeadStatus.Lost, CreatedBy = 1 },
            new Lead { Id = 2, CompanyName = "Globex", ExternalId = "tuyere:sub-2", Status = LeadStatus.New, CreatedBy = 1 });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(
            new GetLeadsQuery(LeadStatus.New, null, "tuyere:sub-1"), CancellationToken.None);

        result.Should().BeEmpty("both filters apply — the lead exists but is not in the requested status");
    }
}
