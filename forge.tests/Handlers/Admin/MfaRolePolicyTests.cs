using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

public class MfaRolePolicyTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<ApplicationUser> SeedUserAsync(string email, params string[] roles)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Policy",
            LastName = "Probe",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        foreach (var role in roles)
            await GrantAsync(user, role);
        return user;
    }

    private async Task GrantAsync(ApplicationUser user, string roleName)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role is null)
        {
            role = new IdentityRole<int>(roleName) { NormalizedName = roleName.ToUpperInvariant() };
            _db.Roles.Add(role);
            await _db.SaveChangesAsync();
        }
        _db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = role.Id });
        await _db.SaveChangesAsync();
    }

    private SetMfaPolicyHandler SetHandler() => new(_db, MockRoles().Object);

    private MfaService Mfa() => new(
        _db, Mock.Of<ITokenEncryptionService>(), Mock.Of<ITokenService>(), Mock.Of<ISessionStore>(),
        new MemoryCache(new MemoryCacheOptions()), Mock.Of<IMfaTrustedDeviceTokenService>(),
        NullLogger<MfaService>.Instance);

    [Fact]
    public async Task Policy_KeepsARoleThatNobodyHoldsYet()
    {
        await SeedUserAsync("worker@forge.test", "ProductionWorker");

        await SetHandler().Handle(new SetMfaPolicyCommand(["Controller", "Admin"]), CancellationToken.None);

        var policy = await new GetMfaPolicyHandler(_db).Handle(new GetMfaPolicyQuery(), CancellationToken.None);
        policy.RequiredRoles.Should().BeEquivalentTo("Admin", "Controller");
    }

    [Fact]
    public async Task UserWhoGainsAPolicyRoleLater_IsEnforced()
    {
        await SetHandler().Handle(new SetMfaPolicyCommand(["Controller"]), CancellationToken.None);
        var later = await SeedUserAsync("new.controller@forge.test");

        (await Mfa().GetMfaStatusAsync(later.Id, CancellationToken.None)).IsEnforcedByPolicy.Should().BeFalse();

        await GrantAsync(later, "Controller");

        (await Mfa().GetMfaStatusAsync(later.Id, CancellationToken.None)).IsEnforcedByPolicy.Should().BeTrue();
        (await Mfa().IsMfaRequiredAsync(later.Id, CancellationToken.None)).Should().BeTrue();
        var compliance = await new GetMfaPolicyStatusHandler(_db, MockRoles().Object)
            .Handle(new GetMfaPolicyStatusQuery(), CancellationToken.None);
        compliance.Single(u => u.UserId == later.Id).IsEnforcedByPolicy.Should().BeTrue();
    }

    [Fact]
    public async Task ClearingThePolicy_StopsEnforcement_EvenWithAStaleFlag()
    {
        var user = await SeedUserAsync("pm@forge.test", "PM");
        await SetHandler().Handle(new SetMfaPolicyCommand(["PM"]), CancellationToken.None);
        (await Mfa().GetMfaStatusAsync(user.Id, CancellationToken.None)).IsEnforcedByPolicy.Should().BeTrue();

        await SetHandler().Handle(new SetMfaPolicyCommand([]), CancellationToken.None);

        (await Mfa().GetMfaStatusAsync(user.Id, CancellationToken.None)).IsEnforcedByPolicy.Should().BeFalse();
    }

    [Fact]
    public async Task InstallWithoutAStoredPolicy_FallsBackToThePerUserFlag()
    {
        var user = await SeedUserAsync("legacy@forge.test", "PM");
        user.MfaEnforcedByPolicy = true;
        await _db.SaveChangesAsync();

        (await MfaRolePolicy.IsEnforcedAsync(_db, user, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task EnforcedUser_CannotDisableMfa()
    {
        var user = await SeedUserAsync("admin@forge.test", "Admin");
        user.MfaEnabled = true;
        await _db.SaveChangesAsync();
        await SetHandler().Handle(new SetMfaPolicyCommand(["Admin"]), CancellationToken.None);

        var act = () => Mfa().DisableMfaAsync(user.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.Users.FindAsync(user.Id))!.MfaEnabled.Should().BeTrue();
    }

    private Mock<UserManager<ApplicationUser>> MockRoles()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .Returns((ApplicationUser u) => Task.FromResult<IList<string>>(_db.UserRoles
                .Where(ur => ur.UserId == u.Id)
                .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToList()));
        return userManager;
    }

    public void Dispose() => _db.Dispose();
}
