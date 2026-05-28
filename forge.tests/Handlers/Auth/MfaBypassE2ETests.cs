using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OtpNet;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Auth;

/// <summary>
/// F-054 — end-to-end proof that MFA *supplements* the password (it no longer
/// *replaces* it) and the no-password full-JWT bypass is closed. Wires the real
/// <see cref="MfaPreAuthTokenService"/>, <see cref="CreateMfaChallengeHandler"/>
/// and <see cref="MfaService"/> against an InMemory EF store + a real
/// <see cref="IMemoryCache"/>, then drives the login → challenge → validate
/// handshake.
///
/// This is the F-054 DoD / regression guard (H-017 D2 + D6):
///   POSITIVE — pre-auth token (proof the password passed) + valid TOTP → full JWT.
///   NEGATIVE — no/forged pre-auth token → no challenge → no JWT; a fabricated
///              challenge token → no JWT; a full access JWT can't act as a
///              pre-auth token. The password step cannot be skipped.
/// </summary>
public class MfaBypassE2ETests : IDisposable
{
    private const int UserId = 1;
    private const string Secret = "JBSWY3DPEHPK3PXP";
    private const string TestJwtKey = "e2e-test-jwt-signing-key-at-least-32-chars!!";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ITokenEncryptionService _encryption = new PassthroughEncryption();
    private readonly MfaService _mfa;
    private readonly MfaPreAuthTokenService _preAuth;
    private readonly CreateMfaChallengeHandler _challengeHandler;

    public MfaBypassE2ETests()
    {
        _db = TestDbContextFactory.Create();
        _cache = new MemoryCache(new MemoryCacheOptions());

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = "forge",
                ["Jwt:Audience"] = "forge-ui",
            })
            .Build();

        var tokenService = new JwtTokenService(config);

        var sessionStore = new Mock<ISessionStore>();
        sessionStore
            .Setup(x => x.CreateSessionAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mfa = new MfaService(
            _db, _encryption, tokenService, sessionStore.Object,
            _cache, NullLogger<MfaService>.Instance);

        _preAuth = new MfaPreAuthTokenService(config);
        _challengeHandler = new CreateMfaChallengeHandler(_mfa, _preAuth);
    }

    private async Task SeedMfaEnrolledUserAsync()
    {
        _db.Users.Add(new ApplicationUser
        {
            Id = UserId,
            Email = "mfa.user@forge.local",
            UserName = "mfa.user@forge.local",
            FirstName = "Mfa",
            LastName = "User",
            Initials = "MU",
            AvatarColor = "#000000",
            IsActive = true,
            MfaEnabled = true,
        });
        _db.UserMfaDevices.Add(new UserMfaDevice
        {
            UserId = UserId,
            DeviceType = MfaDeviceType.Totp,
            EncryptedSecret = _encryption.Encrypt(Secret),
            DeviceName = "Authenticator",
            IsVerified = true,
            IsDefault = true,
        });
        await _db.SaveChangesAsync();
    }

    // G-MFA-3: the server now Base32-DECODES the secret (matching a real authenticator app);
    // compute the expected code the same way (±1 step tolerance covers boundaries).
    private static string CurrentCode() =>
        new Totp(Base32Encoding.ToBytes(Secret), step: 30, totpSize: 6).ComputeTotp();

    [Fact]
    public async Task FullFlow_PreAuthTokenPlusValidTotp_YieldsFullJwt()
    {
        await SeedMfaEnrolledUserAsync();

        // 1) Login proved the password → issues the pre-auth token.
        //    (LoginHandlerTests proves Login emits exactly this for an MFA user.)
        var pendingToken = _preAuth.Issue(UserId);

        // 2) Challenge requires + consumes the pre-auth token.
        var challenge = await _challengeHandler.Handle(
            new CreateMfaChallengeCommand(pendingToken), CancellationToken.None);
        challenge.ChallengeToken.Should().NotBeNullOrEmpty();

        // 3) Validate with a real TOTP → full access JWT issued.
        var result = await _mfa.ValidateChallengeAsync(
            challenge.ChallengeToken, CurrentCode(), rememberDevice: false, CancellationToken.None);

        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NoOrForgedPreAuthToken_CannotCreateChallenge()
    {
        await SeedMfaEnrolledUserAsync();

        // Attacker skips /auth/login → has no valid pre-auth token.
        var act = () => _challengeHandler.Handle(
            new CreateMfaChallengeCommand("forged-or-absent-token"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task FabricatedChallengeToken_YieldsNoJwt()
    {
        await SeedMfaEnrolledUserAsync();

        // Even with a valid current TOTP, a challenge token that was never issued
        // (because no pre-auth token gated a challenge) cannot mint a JWT.
        var result = await _mfa.ValidateChallengeAsync(
            "challenge-token-never-issued", CurrentCode(), rememberDevice: false, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FullAccessJwt_CannotBeUsedAsPreAuthToken()
    {
        await SeedMfaEnrolledUserAsync();

        var fullJwt = new JwtTokenService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = "forge",
                ["Jwt:Audience"] = "forge-ui",
            })
            .Build())
            .GenerateToken(UserId, "mfa.user@forge.local", "Mfa", "User", "MU", "#000000",
                new List<string> { "Admin" });

        var act = () => _challengeHandler.Handle(
            new CreateMfaChallengeCommand(fullJwt.Token), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private sealed class PassthroughEncryption : ITokenEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }

    public void Dispose() => _db.Dispose();
}
