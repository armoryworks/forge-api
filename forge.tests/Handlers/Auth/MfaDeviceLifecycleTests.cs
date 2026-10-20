using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Auth;

public class MfaDeviceLifecycleTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private MfaService Mfa()
    {
        var encryption = new Mock<ITokenEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        return new MfaService(
            _db, encryption.Object, Mock.Of<ITokenService>(), Mock.Of<ISessionStore>(),
            new MemoryCache(new MemoryCacheOptions()), Mock.Of<IMfaTrustedDeviceTokenService>(),
            NullLogger<MfaService>.Instance);
    }

    private async Task<ApplicationUser> SeedUserAsync(bool mfaEnabled = false, int recoveryCodes = 0)
    {
        var user = new ApplicationUser
        {
            UserName = "sec@example.com", Email = "sec@example.com",
            FirstName = "Sec", LastName = "User", IsActive = true,
            MfaEnabled = mfaEnabled, MfaRecoveryCodesRemaining = recoveryCodes,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<UserMfaDevice> SeedDeviceAsync(int userId, bool verified, bool isDefault = false)
    {
        var device = new UserMfaDevice
        {
            UserId = userId, DeviceType = MfaDeviceType.Totp, EncryptedSecret = "S",
            DeviceName = "Authenticator App", IsVerified = verified, IsDefault = isDefault,
        };
        _db.UserMfaDevices.Add(device);
        await _db.SaveChangesAsync();
        return device;
    }

    [Fact]
    public async Task BeginSetup_RetiresEarlierAbandonedSetups_AndKeepsVerifiedDevices()
    {
        var user = await SeedUserAsync(mfaEnabled: true);
        var verified = await SeedDeviceAsync(user.Id, verified: true, isDefault: true);
        await SeedDeviceAsync(user.Id, verified: false);

        var setup = await Mfa().BeginTotpSetupAsync(user.Id, null, CancellationToken.None);

        var live = await _db.UserMfaDevices.Where(d => d.UserId == user.Id).Select(d => d.Id).ToListAsync();
        live.Should().BeEquivalentTo([verified.Id, setup.DeviceId]);
    }

    [Fact]
    public async Task Status_ListsOnlyVerifiedDevices_DefaultFirst()
    {
        var user = await SeedUserAsync(mfaEnabled: true, recoveryCodes: 10);
        var second = await SeedDeviceAsync(user.Id, verified: true);
        var main = await SeedDeviceAsync(user.Id, verified: true, isDefault: true);
        await SeedDeviceAsync(user.Id, verified: false);

        var status = await Mfa().GetMfaStatusAsync(user.Id, CancellationToken.None);

        status.Devices.Select(d => d.Id).Should().Equal(main.Id, second.Id);
        status.RecoveryCodesRemaining.Should().Be(10);
    }

    [Fact]
    public async Task Status_ReportsNoRecoveryCodesWhenMfaIsOff()
    {
        var user = await SeedUserAsync(mfaEnabled: false, recoveryCodes: 10);

        var status = await Mfa().GetMfaStatusAsync(user.Id, CancellationToken.None);

        status.RecoveryCodesRemaining.Should().Be(0);
    }

    [Fact]
    public async Task RemovingLastVerifiedDevice_TurnsMfaOffAndRetiresRecoveryCodes()
    {
        var user = await SeedUserAsync(mfaEnabled: true, recoveryCodes: 10);
        var device = await SeedDeviceAsync(user.Id, verified: true, isDefault: true);
        _db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = user.Id, CodeHash = "h" });
        await _db.SaveChangesAsync();

        await Mfa().RemoveDeviceAsync(user.Id, device.Id, CancellationToken.None);

        var reloaded = await _db.Users.SingleAsync(u => u.Id == user.Id);
        reloaded.MfaEnabled.Should().BeFalse();
        reloaded.MfaRecoveryCodesRemaining.Should().Be(0);
        (await _db.MfaRecoveryCodes.AnyAsync(c => c.UserId == user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateRecoveryCodes_WhenMfaIsOff_IsRefusedAndKeepsExistingCodes()
    {
        var user = await SeedUserAsync(mfaEnabled: false);
        _db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = user.Id, CodeHash = "h" });
        await _db.SaveChangesAsync();

        var act = () => Mfa().GenerateRecoveryCodesAsync(user.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.MfaRecoveryCodes.CountAsync(c => c.UserId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_WhenMfaIsOn_IssuesAFullSet()
    {
        var user = await SeedUserAsync(mfaEnabled: true);

        var result = await Mfa().GenerateRecoveryCodesAsync(user.Id, CancellationToken.None);

        result.Codes.Should().HaveCount(10);
        (await _db.Users.SingleAsync(u => u.Id == user.Id)).MfaRecoveryCodesRemaining.Should().Be(10);
    }
}
