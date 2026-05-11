using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using QBEngineer.Api.Capabilities.Discovery.Bundles;
using QBEngineer.Api.Features.Presets.Apply.Layers;
using QBEngineer.Api.Services;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Integrations;
using QBEngineer.Tests.Helpers;
using QBEngineer.Tests.Integrations;

namespace QBEngineer.Tests.Services;

/// <summary>
/// Pro Services rollout — handler-level tests for the cloud-folder
/// auto-create flow. Uses the in-memory DbContext + the mock cloud
/// provider; the mock dictionary semantics let us assert end-to-end
/// without HTTP.
/// </summary>
public class CloudFolderAutoCreatorTests
{
    [Fact]
    public async Task Returns_Null_When_No_FolderMap_Setting_Configured()
    {
        using var db = TestDbContextFactory.Create();
        var creator = NewCreator(db);

        var result = await creator.AutoCreateAsync(
            "Customer", entityId: 1,
            tokenContext: new Dictionary<string, string> { ["Customer"] = "ACME" },
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_Null_When_No_Suggestion_For_Entity_Type()
    {
        using var db = TestDbContextFactory.Create();
        await SeedFolderMapAsync(db, new[]
        {
            new FolderMapSuggestion("Customer", "/Clients/{Customer}/", new[] { "General" }),
        });
        await SeedActiveProviderAsync(db, "mock");
        var creator = NewCreator(db);

        var result = await creator.AutoCreateAsync(
            "Job", entityId: 1,  // No suggestion for "Job" — skip silently.
            tokenContext: new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Creates_Folder_And_Persists_EntityCloudLink()
    {
        using var db = TestDbContextFactory.Create();
        await SeedFolderMapAsync(db, new[]
        {
            new FolderMapSuggestion("Customer", "/Clients/{Customer}/",
                new[] { "00-General", "01-Contracts" }),
        });
        var providerId = await SeedActiveProviderAsync(db, "mock");
        var creator = NewCreator(db);

        var result = await creator.AutoCreateAsync(
            "Customer", entityId: 42,
            tokenContext: new Dictionary<string, string> { ["Customer"] = "ACME Industries" },
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Customer", result!.EntityType);
        Assert.Equal(42, result.EntityId);
        Assert.Equal(providerId, result.ProviderId);
        Assert.Equal("auto_create", result.CreatedVia);
        Assert.False(string.IsNullOrEmpty(result.FolderExternalId));

        var persisted = await db.EntityCloudLinks.FirstAsync(l =>
            l.EntityType == "Customer" && l.EntityId == 42);
        Assert.Equal(result.Id, persisted.Id);
    }

    [Fact]
    public async Task Sanitizes_Customer_Name_With_Slashes()
    {
        using var db = TestDbContextFactory.Create();
        await SeedFolderMapAsync(db, new[]
        {
            new FolderMapSuggestion("Customer", "/Clients/{Customer}/", new string[0]),
        });
        await SeedActiveProviderAsync(db, "mock");
        var creator = NewCreator(db);

        var result = await creator.AutoCreateAsync(
            "Customer", entityId: 1,
            tokenContext: new Dictionary<string, string> { ["Customer"] = "ACME / Inc" },
            CancellationToken.None);

        Assert.NotNull(result);
        // Path should be "/Clients/ACME - Inc/" (slash sanitized to dash by resolver).
        Assert.Contains("ACME - Inc", result!.FolderPath ?? string.Empty);
        Assert.DoesNotContain("ACME/Inc", result.FolderPath ?? string.Empty);
    }

    [Fact]
    public async Task Skips_When_Suggestion_Has_AutoCreateOnEntityCreate_False()
    {
        using var db = TestDbContextFactory.Create();
        await SeedFolderMapAsync(db, new[]
        {
            new FolderMapSuggestion(
                "Customer", "/Clients/{Customer}/", new[] { "General" },
                AutoCreateOnEntityCreate: false),
        });
        await SeedActiveProviderAsync(db, "mock");
        var creator = NewCreator(db);

        var result = await creator.AutoCreateAsync(
            "Customer", entityId: 1,
            tokenContext: new Dictionary<string, string> { ["Customer"] = "ACME" },
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_Null_When_No_Active_Provider()
    {
        using var db = TestDbContextFactory.Create();
        await SeedFolderMapAsync(db, new[]
        {
            new FolderMapSuggestion("Customer", "/Clients/{Customer}/", new string[0]),
        });
        // Note: no SeedActiveProviderAsync call.
        var creator = NewCreator(db);

        var result = await creator.AutoCreateAsync(
            "Customer", entityId: 1,
            tokenContext: new Dictionary<string, string> { ["Customer"] = "ACME" },
            CancellationToken.None);

        Assert.Null(result);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static CloudFolderAutoCreator NewCreator(QBEngineer.Data.Context.AppDbContext db)
    {
        var mockProvider = new MockCloudStorageIntegrationService(
            NullLogger<MockCloudStorageIntegrationService>.Instance);
        var resolver = new CloudStorageResolver(
            new QBEngineer.Core.Interfaces.ICloudStorageIntegrationService[] { mockProvider },
            NullLogger<CloudStorageResolver>.Instance);
        // Token manager wiring for tests: the mock provider's tokens are
        // pass-through, so the token manager returns "mock-token" without
        // touching encryption. PassThroughTokenEncryptionService doesn't
        // actually encrypt — it returns plaintext.
        var encryption = new PassThroughTokenEncryptionService();
        var tokenManager = new CloudStorageTokenManager(
            db, encryption, resolver,
            NullLogger<CloudStorageTokenManager>.Instance);
        return new CloudFolderAutoCreator(
            db,
            new FolderPathResolver(),
            resolver,
            tokenManager,
            NullLogger<CloudFolderAutoCreator>.Instance);
    }

    /// <summary>Test-only no-op encryption: plaintext in, plaintext out.</summary>
    private sealed class PassThroughTokenEncryptionService : QBEngineer.Core.Interfaces.ITokenEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }

    private static async Task SeedFolderMapAsync(
        QBEngineer.Data.Context.AppDbContext db,
        IReadOnlyList<FolderMapSuggestion> suggestions)
    {
        db.SystemSettings.Add(new SystemSetting
        {
            Key = FolderMapBundleApplier.FolderMapSettingKey,
            Value = JsonSerializer.Serialize(suggestions),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedActiveProviderAsync(
        QBEngineer.Data.Context.AppDbContext db,
        string providerCode)
    {
        var provider = new CloudStorageProvider
        {
            ProviderCode = providerCode,
            Mode = CloudStorageProviderMode.ServiceAccount,
            IsActive = true,
            OAuthTokenEncrypted = providerCode == "mock" ? null : "encrypted-token-placeholder",
        };
        db.CloudStorageProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider.Id;
    }
}
