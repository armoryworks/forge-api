using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class PartRevisionCreatedByTests
{
    private static readonly DateTimeOffset Effective = new(2026, 10, 8, 15, 30, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private CreatePartRevisionHandler HandlerFor(string? userId)
    {
        var httpContext = new Mock<IHttpContextAccessor>();
        var claims = userId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        httpContext.Setup(h => h.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        });
        return new CreatePartRevisionHandler(
            _db, new PartRepository(_db, Mock.Of<IPartPricingResolver>()), httpContext.Object);
    }

    private async Task<ApplicationUser> SeedUserAsync(string firstName, string lastName)
    {
        var user = new ApplicationUser
        {
            UserName = $"{firstName}.{lastName}@t.com".ToLowerInvariant(),
            Email = $"{firstName}.{lastName}@t.com".ToLowerInvariant(),
            FirstName = firstName,
            LastName = lastName,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Part> SeedPartAsync()
    {
        var part = new Part
        {
            PartNumber = "PRT-1",
            Name = "Bracket",
            Revision = "A",
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    [Fact]
    public async Task Create_records_the_current_user_and_returns_their_name()
    {
        var user = await SeedUserAsync("Dana", "Reyes");
        var part = await SeedPartAsync();

        var result = await HandlerFor(user.Id.ToString()).Handle(
            new CreatePartRevisionCommand(part.Id, "B", "Thicker wall", null, Effective), CancellationToken.None);

        result.CreatedByName.Should().Be("Dana Reyes");
        (await _db.PartRevisions.AsNoTracking().SingleAsync()).CreatedBy.Should().Be(user.Id);
    }

    [Fact]
    public async Task Create_without_a_signed_in_user_leaves_the_creator_empty()
    {
        var part = await SeedPartAsync();

        var result = await HandlerFor(null).Handle(
            new CreatePartRevisionCommand(part.Id, "B", null, null, Effective), CancellationToken.None);

        result.CreatedByName.Should().BeNull();
        (await _db.PartRevisions.AsNoTracking().SingleAsync()).CreatedBy.Should().BeNull();
    }

    [Fact]
    public async Task Create_ignores_a_user_id_that_does_not_exist()
    {
        var part = await SeedPartAsync();

        var result = await HandlerFor("9999").Handle(
            new CreatePartRevisionCommand(part.Id, "B", null, null, Effective), CancellationToken.None);

        result.CreatedByName.Should().BeNull();
        (await _db.PartRevisions.AsNoTracking().SingleAsync()).CreatedBy.Should().BeNull();
    }

    [Fact]
    public async Task Get_returns_the_creator_name_and_null_for_historic_revisions()
    {
        var user = await SeedUserAsync("Dana", "Reyes");
        var part = await SeedPartAsync();
        _db.PartRevisions.AddRange(
            new PartRevision { PartId = part.Id, Revision = "A", EffectiveDate = Effective.AddDays(-30) },
            new PartRevision { PartId = part.Id, Revision = "B", EffectiveDate = Effective, IsCurrent = true, CreatedBy = user.Id });
        await _db.SaveChangesAsync();

        var result = await new GetPartRevisionsHandler(_db).Handle(new GetPartRevisionsQuery(part.Id), CancellationToken.None);

        result.Select(r => (r.Revision, r.CreatedByName)).Should().Equal(("B", "Dana Reyes"), ("A", (string?)null));
    }
}
