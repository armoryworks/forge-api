using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Replenishment;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Replenishment;

public class GetReplenishmentAssigneeCandidatesTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private async Task<ApplicationUser> SeedUserAsync(string firstName, string lastName, string? role, bool active = true)
    {
        var user = new ApplicationUser
        {
            UserName = $"{lastName}@test.local",
            Email = $"{lastName}@test.local",
            FirstName = firstName,
            LastName = lastName,
            Initials = "XX",
            AvatarColor = "#888",
            IsActive = active,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (role is not null)
        {
            var roleRow = await _db.Roles.FirstOrDefaultAsync(r => r.Name == role);
            if (roleRow is null)
            {
                roleRow = new IdentityRole<int> { Name = role, NormalizedName = role.ToUpperInvariant() };
                _db.Roles.Add(roleRow);
                await _db.SaveChangesAsync();
            }
            _db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = roleRow.Id });
            await _db.SaveChangesAsync();
        }

        return user;
    }

    [Fact]
    public async Task Handle_ReturnsOnlyActiveAdminsAndManagers_SortedByLastName()
    {
        var manager = await SeedUserAsync("Pat", "Young", "Manager");
        var admin = await SeedUserAsync("Lee", "Adams", "Admin");
        await SeedUserAsync("Kim", "Baker", "Engineer");
        await SeedUserAsync("Ray", "Cole", "ProductionWorker");
        await SeedUserAsync("Dee", "Diaz", "Manager", active: false);
        await SeedUserAsync("Max", "Ellis", null);

        var result = await new GetReplenishmentAssigneeCandidatesHandler(_db)
            .Handle(new GetReplenishmentAssigneeCandidatesQuery(), default);

        result.Select(c => c.Id).Should().Equal(admin.Id, manager.Id);
        result.Select(c => c.Name).Should().Equal("Lee Adams", "Pat Young");
    }

    [Fact]
    public async Task Handle_ListsAUserWithBothRolesOnce()
    {
        var both = await SeedUserAsync("Jo", "Fox", "Admin");
        var managerRole = new IdentityRole<int> { Name = "Manager", NormalizedName = "MANAGER" };
        _db.Roles.Add(managerRole);
        await _db.SaveChangesAsync();
        _db.UserRoles.Add(new IdentityUserRole<int> { UserId = both.Id, RoleId = managerRole.Id });
        await _db.SaveChangesAsync();

        var result = await new GetReplenishmentAssigneeCandidatesHandler(_db)
            .Handle(new GetReplenishmentAssigneeCandidatesQuery(), default);

        result.Should().ContainSingle().Which.Id.Should().Be(both.Id);
    }
}
