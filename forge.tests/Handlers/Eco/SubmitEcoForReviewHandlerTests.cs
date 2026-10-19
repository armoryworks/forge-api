using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Eco;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Eco;

public class SubmitEcoForReviewHandlerTests
{
    private static IHttpContextAccessor Http(int userId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        },
    };

    [Fact]
    public async Task Draft_eco_is_submitted_then_approved()
    {
        using var db = TestDbContextFactory.Create();
        var eco = new EngineeringChangeOrder { EcoNumber = "ECO-1", Title = "Bore chamfer", Description = "Add chamfer", RequestedById = 3 };
        db.EngineeringChangeOrders.Add(eco);
        await db.SaveChangesAsync();

        await new SubmitEcoForReviewHandler(db).Handle(new SubmitEcoForReviewCommand(eco.Id), CancellationToken.None);
        eco.Status.Should().Be(EcoStatus.Review);

        await new ApproveEcoHandler(db, Http(5), new SystemClock()).Handle(new ApproveEcoCommand(eco.Id), CancellationToken.None);
        eco.Status.Should().Be(EcoStatus.Approved);
        eco.ApprovedById.Should().Be(5);
    }

    [Theory]
    [InlineData(EcoStatus.Review)]
    [InlineData(EcoStatus.Approved)]
    [InlineData(EcoStatus.Implemented)]
    [InlineData(EcoStatus.Cancelled)]
    public async Task Only_a_draft_eco_can_be_submitted(EcoStatus status)
    {
        using var db = TestDbContextFactory.Create();
        var eco = new EngineeringChangeOrder { EcoNumber = "ECO-2", Title = "T", Description = "D", RequestedById = 3, Status = status };
        db.EngineeringChangeOrders.Add(eco);
        await db.SaveChangesAsync();

        var act = () => new SubmitEcoForReviewHandler(db).Handle(new SubmitEcoForReviewCommand(eco.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        eco.Status.Should().Be(status);
    }

    [Fact]
    public async Task Missing_eco_is_not_found()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => new SubmitEcoForReviewHandler(db).Handle(new SubmitEcoForReviewCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
