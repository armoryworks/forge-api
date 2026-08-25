using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class PasskeyServiceTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
    }

    private const string Origin = "https://shop.example.com";

    private static (PasskeyService service, AppDbContext db, IMemoryCache cache) Create()
    {
        var db = TestDbContextFactory.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PasskeyService(
            db, cache, new FixedClock(), NullLogger<PasskeyService>.Instance);
        return (service, db, cache);
    }

    private static async Task<ApplicationUser> SeedUserAsync(AppDbContext db)
    {
        var user = new ApplicationUser
        {
            UserName = "op@forge.local",
            Email = "op@forge.local",
            FirstName = "Ada",
            LastName = "Ramos",
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Registration_options_carry_user_and_relying_party_and_are_cached()
    {
        var (service, db, cache) = Create();
        var user = await SeedUserAsync(db);

        var options = await service.BeginRegistrationAsync(user.Id, Origin, CancellationToken.None);

        options.Rp.Id.Should().Be("shop.example.com");
        options.User.Name.Should().Be("op@forge.local");
        options.Challenge.Should().NotBeEmpty();
        cache.TryGetValue<string>($"passkey:reg:{user.Id}", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Assertion_options_are_null_without_registered_passkeys()
    {
        var (service, db, _) = Create();
        var user = await SeedUserAsync(db);

        (await service.BeginAssertionAsync(user.Id, Origin, CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task Assertion_options_list_the_registered_credential()
    {
        var (service, db, cache) = Create();
        var user = await SeedUserAsync(db);

        var credentialId = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);
        db.UserMfaDevices.Add(new UserMfaDevice
        {
            UserId = user.Id,
            DeviceType = MfaDeviceType.WebAuthn,
            DeviceName = "Passkey",
            IsVerified = true,
            EncryptedSecret = string.Empty,
            CredentialId = credentialId,
            PublicKey = Convert.ToBase64String([9, 9, 9]),
            SignCount = 0,
        });
        await db.SaveChangesAsync();

        var options = await service.BeginAssertionAsync(user.Id, Origin, CancellationToken.None);

        options.Should().NotBeNull();
        options!.AllowCredentials.Should().ContainSingle(
            c => Convert.ToBase64String(c.Id) == credentialId);
        cache.TryGetValue<string>($"passkey:assert:{user.Id}", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Completing_an_assertion_without_pending_options_fails_closed()
    {
        var (service, db, _) = Create();
        var user = await SeedUserAsync(db);

        var response = new Fido2NetLib.AuthenticatorAssertionRawResponse
        {
            RawId = [1, 2, 3],
        };

        (await service.CompleteAssertionAsync(user.Id, Origin, response, CancellationToken.None))
            .Should().BeFalse();
    }
}
