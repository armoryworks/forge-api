using FluentAssertions;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class TeamMemberCountTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<Team> SeedTeamAsync()
    {
        var team = new Team { Name = "CNC" };
        _db.Teams.Add(team);
        await _db.SaveChangesAsync();
        _db.Users.AddRange(
            new ApplicationUser { FirstName = "Pat", LastName = "Worker", UserName = "pat@example.com", IsActive = true, TeamId = team.Id },
            new ApplicationUser { FirstName = "Sam", LastName = "Worker", UserName = "sam@example.com", IsActive = true, TeamId = team.Id },
            new ApplicationUser { FirstName = "Con", LastName = "Tractor", UserName = "con@example.com", IsActive = true, TeamId = team.Id, IsNonEmployee = true },
            new ApplicationUser { FirstName = "Old", LastName = "Hand", UserName = "old@example.com", IsActive = false, TeamId = team.Id });
        await _db.SaveChangesAsync();
        return team;
    }

    [Fact]
    public async Task GetTeams_CountsOnlyActiveEmployees()
    {
        var team = await SeedTeamAsync();

        var teams = await new GetTeamsHandler(_db).Handle(new GetTeamsQuery(), CancellationToken.None);

        teams.Single(t => t.Id == team.Id).MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task UpdateTeam_CountsOnlyActiveEmployees()
    {
        var team = await SeedTeamAsync();

        var updated = await new UpdateTeamHandler(_db).Handle(
            new UpdateTeamCommand(team.Id, "CNC", null, null, true), CancellationToken.None);

        updated.MemberCount.Should().Be(2);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
