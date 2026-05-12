using FluentAssertions;
using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

/// <summary>
/// Verifies the receiving-inspection-templates list endpoint feeds the
/// <c>&lt;app-entity-picker&gt;</c> shape (paged response, search filter,
/// active rows only).
/// </summary>
public class GetReceivingInspectionTemplatesHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly GetReceivingInspectionTemplatesHandler _handler;

    public GetReceivingInspectionTemplatesHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetReceivingInspectionTemplatesHandler(_db);

        _db.QcChecklistTemplates.AddRange(
            new QcChecklistTemplate { Name = "Receiving — Steel Round Stock", Description = "Checklist for steel rounds", IsActive = true },
            new QcChecklistTemplate { Name = "Receiving — Aluminum Sheet", Description = "Checklist for aluminum sheet", IsActive = true },
            new QcChecklistTemplate { Name = "Receiving — Bronze Bar", Description = "Bronze bar receipts", IsActive = true },
            new QcChecklistTemplate { Name = "Inactive Template", Description = null, IsActive = false }
        );
        _db.SaveChanges();
    }

    [Fact]
    public async Task Handle_NoFilter_ReturnsActiveTemplatesPaged()
    {
        var query = new GetReceivingInspectionTemplatesQuery(null, 1, 20);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result.Items.Should().HaveCount(3);
        result.Items.Select(i => i.Name).Should().NotContain("Inactive Template");
        result.TotalCount.Should().Be(3);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_Search_FiltersByNameAndDescriptionCaseInsensitive()
    {
        var query = new GetReceivingInspectionTemplatesQuery("aluminum", 1, 20);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Receiving — Aluminum Sheet");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Search_MatchesDescription()
    {
        var query = new GetReceivingInspectionTemplatesQuery("BRONZE BAR receipts", 1, 20);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Receiving — Bronze Bar");
    }

    [Fact]
    public async Task Handle_PageSize_RespectsRequestedSize()
    {
        var query = new GetReceivingInspectionTemplatesQuery(null, 1, 2);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(3);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task Handle_PageSize_CapsAtHundred()
    {
        var query = new GetReceivingInspectionTemplatesQuery(null, 1, 5000);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_InvalidPage_FallsBackToOne()
    {
        var query = new GetReceivingInspectionTemplatesQuery(null, 0, 10);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Page.Should().Be(1);
    }
}
