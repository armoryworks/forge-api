using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using QBEngineer.Api.Services;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Integrations;
using QBEngineer.Tests.Helpers;

namespace QBEngineer.Tests.Services;

/// <summary>
/// Pro Services rollout — tests for the OAuth token manager that wraps
/// the encrypt / decrypt + refresh-on-near-expiry logic for cloud
/// providers. Uses pass-through encryption + a stub provider so we can
/// drive refresh behavior deterministically without hitting real APIs.
/// </summary>
public class CloudStorageTokenManagerTests
{
    [Fact]
    public async Task Returns_Mock_Token_Without_Decrypt_Or_Refresh()
    {
        using var db = TestDbContextFactory.Create();
        var provider = await SeedMockProviderAsync(db);
        var mgr = NewManager(db);

        var token = await mgr.GetValidAccessTokenAsync(provider, CancellationToken.None);

        Assert.Equal("mock-token", token);
    }

    [Fact]
    public async Task Returns_Decrypted_Token_When_Not_Near_Expiry()
    {
        using var db = TestDbContextFactory.Create();
        var stubProvider = new StubProvider();
        var provider = await SeedRealProviderAsync(db, "gdrive",
            accessToken: "valid-access-1",
            refreshToken: "refresh-1",
            expiresIn: TimeSpan.FromHours(1));

        var mgr = NewManager(db, stubProvider);
        var token = await mgr.GetValidAccessTokenAsync(provider, CancellationToken.None);

        Assert.Equal("valid-access-1", token);
        Assert.Equal(0, stubProvider.RefreshCallCount);
    }

    [Fact]
    public async Task Refreshes_Token_When_Near_Expiry()
    {
        using var db = TestDbContextFactory.Create();
        var stubProvider = new StubProvider
        {
            NextAccessToken = "rotated-access",
            NextRefreshToken = "rotated-refresh",
            NextExpiry = DateTimeOffset.UtcNow.AddHours(1),
        };
        var provider = await SeedRealProviderAsync(db, "gdrive",
            accessToken: "expired-access",
            refreshToken: "old-refresh",
            expiresIn: TimeSpan.FromMinutes(1));  // Within 5-minute refresh buffer.

        var mgr = NewManager(db, stubProvider);
        var token = await mgr.GetValidAccessTokenAsync(provider, CancellationToken.None);

        Assert.Equal("rotated-access", token);
        Assert.Equal(1, stubProvider.RefreshCallCount);
        Assert.Equal("old-refresh", stubProvider.LastRefreshToken);

        // Verify the rotation persisted.
        var refreshed = await db.CloudStorageProviders.FirstAsync(p => p.Id == provider.Id);
        Assert.Equal("rotated-access", refreshed.OAuthTokenEncrypted);  // PassThrough encryption = plaintext.
        Assert.Equal("rotated-refresh", refreshed.RefreshTokenEncrypted);
        Assert.NotNull(refreshed.LastConnectedAt);
        Assert.Null(refreshed.LastError);
    }

    [Fact]
    public async Task Returns_Null_When_Provider_Has_No_Tokens()
    {
        using var db = TestDbContextFactory.Create();
        var provider = new CloudStorageProvider
        {
            ProviderCode = "gdrive",
            Mode = CloudStorageProviderMode.ServiceAccount,
            IsActive = true,
            OAuthTokenEncrypted = null,
            RefreshTokenEncrypted = null,
        };
        db.CloudStorageProviders.Add(provider);
        await db.SaveChangesAsync();

        var mgr = NewManager(db, new StubProvider());
        var token = await mgr.GetValidAccessTokenAsync(provider, CancellationToken.None);

        Assert.Null(token);
    }

    [Fact]
    public async Task Records_LastError_When_Refresh_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var stubProvider = new StubProvider { ThrowOnRefresh = true };
        var provider = await SeedRealProviderAsync(db, "gdrive",
            accessToken: "expired-access",
            refreshToken: "old-refresh",
            expiresIn: TimeSpan.FromMinutes(1));

        var mgr = NewManager(db, stubProvider);
        var token = await mgr.GetValidAccessTokenAsync(provider, CancellationToken.None);

        Assert.Null(token);
        var after = await db.CloudStorageProviders.FirstAsync(p => p.Id == provider.Id);
        Assert.NotNull(after.LastError);
        Assert.Contains("refresh failed", after.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserCloudStorageLink_Returns_Decrypted_Token_When_Valid()
    {
        using var db = TestDbContextFactory.Create();
        var provider = await SeedRealProviderAsync(db, "gdrive",
            accessToken: "provider-access", refreshToken: "provider-refresh",
            expiresIn: TimeSpan.FromHours(1));
        var link = new UserCloudStorageLink
        {
            UserId = Guid.NewGuid(),
            ProviderId = provider.Id,
            OAuthTokenEncrypted = "user-access-token",
            RefreshTokenEncrypted = "user-refresh-token",
            TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Provider = provider,
        };
        db.UserCloudStorageLinks.Add(link);
        await db.SaveChangesAsync();

        var mgr = NewManager(db, new StubProvider());
        var token = await mgr.GetValidAccessTokenAsync(link, CancellationToken.None);

        Assert.Equal("user-access-token", token);
    }

    [Fact]
    public async Task UserCloudStorageLink_Refreshes_When_Near_Expiry()
    {
        using var db = TestDbContextFactory.Create();
        var stubProvider = new StubProvider
        {
            NextAccessToken = "rotated-user-access",
            NextRefreshToken = "rotated-user-refresh",
            NextExpiry = DateTimeOffset.UtcNow.AddHours(1),
        };
        var provider = await SeedRealProviderAsync(db, "gdrive",
            accessToken: "provider-access", refreshToken: "provider-refresh",
            expiresIn: TimeSpan.FromHours(1));
        var link = new UserCloudStorageLink
        {
            UserId = Guid.NewGuid(),
            ProviderId = provider.Id,
            OAuthTokenEncrypted = "expired-user-access",
            RefreshTokenEncrypted = "old-user-refresh",
            TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),  // near expiry
        };
        db.UserCloudStorageLinks.Add(link);
        await db.SaveChangesAsync();

        var mgr = NewManager(db, stubProvider);
        var token = await mgr.GetValidAccessTokenAsync(link, CancellationToken.None);

        Assert.Equal("rotated-user-access", token);
        var refreshed = await db.UserCloudStorageLinks.FirstAsync(l => l.Id == link.Id);
        Assert.Equal("rotated-user-access", refreshed.OAuthTokenEncrypted);
        Assert.Equal("rotated-user-refresh", refreshed.RefreshTokenEncrypted);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static CloudStorageTokenManager NewManager(
        QBEngineer.Data.Context.AppDbContext db,
        StubProvider? stubProvider = null)
    {
        var services = new List<ICloudStorageIntegrationService>
        {
            new MockCloudStorageIntegrationService(NullLogger<MockCloudStorageIntegrationService>.Instance),
        };
        if (stubProvider is not null) services.Add(stubProvider);
        var resolver = new CloudStorageResolver(services, NullLogger<CloudStorageResolver>.Instance);
        var encryption = new PassThroughTokenEncryptionService();
        return new CloudStorageTokenManager(
            db, encryption, resolver,
            NullLogger<CloudStorageTokenManager>.Instance);
    }

    private static async Task<CloudStorageProvider> SeedMockProviderAsync(
        QBEngineer.Data.Context.AppDbContext db)
    {
        var provider = new CloudStorageProvider
        {
            ProviderCode = "mock",
            Mode = CloudStorageProviderMode.ServiceAccount,
            IsActive = true,
        };
        db.CloudStorageProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    private static async Task<CloudStorageProvider> SeedRealProviderAsync(
        QBEngineer.Data.Context.AppDbContext db,
        string code,
        string accessToken,
        string refreshToken,
        TimeSpan expiresIn)
    {
        var provider = new CloudStorageProvider
        {
            ProviderCode = code,
            Mode = CloudStorageProviderMode.ServiceAccount,
            IsActive = true,
            OAuthTokenEncrypted = accessToken,    // PassThrough encryption = plaintext.
            RefreshTokenEncrypted = refreshToken,
            TokenExpiresAt = DateTimeOffset.UtcNow.Add(expiresIn),
        };
        db.CloudStorageProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    private sealed class PassThroughTokenEncryptionService : ITokenEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }

    /// <summary>
    /// Test-only ICloudStorageIntegrationService that lets us observe
    /// refresh-call behavior without real HTTP. Returns "gdrive" so the
    /// resolver matches it on the real-provider code path.
    /// </summary>
    private sealed class StubProvider : ICloudStorageIntegrationService
    {
        public string ProviderCode => "gdrive";

        public int RefreshCallCount { get; private set; }
        public string? LastRefreshToken { get; private set; }
        public string NextAccessToken { get; set; } = "next-access";
        public string NextRefreshToken { get; set; } = "next-refresh";
        public DateTimeOffset NextExpiry { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
        public bool ThrowOnRefresh { get; set; }

        public Task<CloudStorageTokenRefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct)
        {
            RefreshCallCount++;
            LastRefreshToken = refreshToken;
            if (ThrowOnRefresh) throw new InvalidOperationException("refresh provider went boom");
            return Task.FromResult(new CloudStorageTokenRefreshResult(
                AccessToken: NextAccessToken,
                RefreshToken: NextRefreshToken,
                ExpiresAt: NextExpiry));
        }

        public Task<CloudFolder> CreateFolderAsync(string a, CreateFolderRequest r, CancellationToken ct) => throw new NotImplementedException();
        public Task<CloudFolder?> GetFolderAsync(string a, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<CloudFolder>> ListChildFoldersAsync(string a, string p, CancellationToken ct) => throw new NotImplementedException();
        public Task<CloudFolder?> FindFolderByPathAsync(string a, string p, CancellationToken ct) => throw new NotImplementedException();
        public Task<CloudStorageHealthResult> HealthCheckAsync(string a, CancellationToken ct) => throw new NotImplementedException();
    }
}
