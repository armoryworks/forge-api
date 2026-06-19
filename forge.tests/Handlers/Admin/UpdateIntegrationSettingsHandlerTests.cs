using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Api.Features.Settings;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

/// <summary>
/// Coverage for the recent integrations-admin fixes:
///   - Save loop atomicity: a mid-loop validation failure rolls back ALL
///     prior persists (previously each <c>SetAsync</c> committed on its
///     own → partial state on first bad field).
///   - <c>minio.public-endpoint</c> propagates to <c>MinioOptions.PublicEndpoint</c>
///     via <c>ApplyMinio</c> (Bug 3 / two-endpoint pattern).
///   - <c>GetIntegrationSettings</c> populates <c>Choices</c> and
///     <c>Description</c> on each <c>IntegrationSettingField</c> so the
///     admin UI can render the mode dropdown.
/// </summary>
public class UpdateIntegrationSettingsHandlerTests
{
    /// <summary>
    /// Shared options instances exposed to tests that assert post-save
    /// propagation across multiple integrations. The MinIO-specific
    /// overload below remains for backwards compatibility with the
    /// earlier tests.
    /// </summary>
    private static UpdateIntegrationSettingsHandler MakeHandler(
        AppDbContextLike db,
        out MinioOptions minio,
        out UspsOptions usps,
        out AiOptions ai,
        out GoogleDriveOptions gdrive)
    {
        var dp = new EphemeralDataProtectionProvider();
        var settings = new SettingsService(db.Db, dp);
        minio = new MinioOptions();
        usps = new UspsOptions();
        ai = new AiOptions();
        gdrive = new GoogleDriveOptions();

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<GetIntegrationSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationSettingsResult(
                ShowSandboxGuides: true,
                Integrations: IntegrationDescriptorCatalog.All
                    .Select(d => new IntegrationStatusModel(
                        Provider: d.Provider,
                        Name: d.Name,
                        Description: d.Description,
                        Icon: d.Icon,
                        IsConfigured: false,
                        Fields: new(),
                        Category: d.Category))
                    .ToList()));

        return new UpdateIntegrationSettingsHandler(
            settings,
            db.Db,
            mediator.Object,
            Options.Create(new SmtpOptions()),
            Options.Create(minio),
            Options.Create(usps),
            Options.Create(new DocuSealOptions()),
            Options.Create(ai),
            Options.Create(gdrive),
            Options.Create(new QuickBooksOptions()),
            Options.Create(new XeroOptions()),
            Options.Create(new FreshBooksOptions()),
            Options.Create(new SageOptions()),
            Options.Create(new NetSuiteOptions()),
            Options.Create(new WaveOptions()),
            Options.Create(new ZohoOptions()));
    }

    private static UpdateIntegrationSettingsHandler MakeHandler(
        AppDbContextLike db,
        out MinioOptions minio)
    {
        var dp = new EphemeralDataProtectionProvider();
        var settings = new SettingsService(db.Db, dp);
        minio = new MinioOptions();

        // The handler ends by re-fetching state via GetIntegrationSettingsQuery
        // and returning the matching IntegrationStatusModel. Mock it to
        // produce a result whose Integrations contains every catalog provider
        // (with empty fields) so .First(provider == request.Provider) finds
        // a match for any test input. We're asserting on DB / IOptions state,
        // not the returned model, so empty fields are fine.
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<GetIntegrationSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationSettingsResult(
                ShowSandboxGuides: true,
                Integrations: IntegrationDescriptorCatalog.All
                    .Select(d => new IntegrationStatusModel(
                        Provider: d.Provider,
                        Name: d.Name,
                        Description: d.Description,
                        Icon: d.Icon,
                        IsConfigured: false,
                        Fields: new(),
                        Category: d.Category))
                    .ToList()));

        return new UpdateIntegrationSettingsHandler(
            settings,
            db.Db,
            mediator.Object,
            Options.Create(new SmtpOptions()),
            Options.Create(minio),
            Options.Create(new UspsOptions()),
            Options.Create(new DocuSealOptions()),
            Options.Create(new AiOptions()),
            Options.Create(new GoogleDriveOptions()),
            Options.Create(new QuickBooksOptions()),
            Options.Create(new XeroOptions()),
            Options.Create(new FreshBooksOptions()),
            Options.Create(new SageOptions()),
            Options.Create(new NetSuiteOptions()),
            Options.Create(new WaveOptions()),
            Options.Create(new ZohoOptions()));
    }

    [Fact]
    public async Task Handle_ValidMinioSave_PersistsAllFields_AndPropagatesToOptions()
    {
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out var minio);

        await handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "minio",
            Settings: new Dictionary<string, string>
            {
                [MinioSettings.KeyMode] = "Real",
                [MinioSettings.KeyEndpoint] = "minio.internal:9000",
                [MinioSettings.KeyPublicEndpoint] = "files.example.com",
                [MinioSettings.KeyAccessKey] = "AKIATEST",
                [MinioSettings.KeySecretKey] = "supersecret",
                [MinioSettings.KeyBucket] = "forge-prod",
                [MinioSettings.KeyUseSsl] = "true",
            }),
            CancellationToken.None);

        // All six rows present.
        var rows = dbScope.Db.SystemSettings.ToList();
        rows.Should().Contain(r => r.Key == MinioSettings.KeyMode);
        rows.Should().Contain(r => r.Key == MinioSettings.KeyEndpoint);
        rows.Should().Contain(r => r.Key == MinioSettings.KeyPublicEndpoint);
        rows.Should().Contain(r => r.Key == MinioSettings.KeyAccessKey);
        rows.Should().Contain(r => r.Key == MinioSettings.KeySecretKey);
        rows.Should().Contain(r => r.Key == MinioSettings.KeyBucket);
        rows.Should().Contain(r => r.Key == MinioSettings.KeyUseSsl);

        // PublicEndpoint propagated to IOptions for hot-reload.
        minio.PublicEndpoint.Should().Be("files.example.com");
        minio.Endpoint.Should().Be("minio.internal:9000");
        minio.AccessKey.Should().Be("AKIATEST");
        minio.UseSsl.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidEnumValueOnMode_RollsBackEverything()
    {
        // Headline regression check: pre-fix, the loop processed fields in
        // order; the first throw left earlier fields persisted and later
        // fields untouched. With BeginTransactionAsync + rollback, a bad
        // mode value should leave the table completely unchanged — no
        // half-saved endpoint, no orphan access key.
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out _);

        // Pre-seed a known endpoint so we can prove rollback restored it.
        dbScope.Db.SystemSettings.Add(new SystemSetting
        {
            Key = MinioSettings.KeyEndpoint,
            Value = "ORIGINAL:9000",
        });
        await dbScope.Db.SaveChangesAsync();

        var act = () => handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "minio",
            Settings: new Dictionary<string, string>
            {
                // Mode is processed first in declaration order. "real"
                // (lowercase) fails the ordinal enum validator — exactly
                // the case the free-text-input bug was producing.
                [MinioSettings.KeyMode] = "real",
                [MinioSettings.KeyEndpoint] = "shouldNotPersist:9000",
                [MinioSettings.KeyAccessKey] = "shouldNotPersist",
            }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must be one of*");

        // Endpoint row should still hold the pre-existing value (NOT
        // "shouldNotPersist:9000"). Access key row should not exist.
        var endpoint = dbScope.Db.SystemSettings
            .FirstOrDefault(s => s.Key == MinioSettings.KeyEndpoint);
        endpoint!.Value.Should().Be(
            "ORIGINAL:9000",
            "rollback must restore the pre-transaction state — a partial save would corrupt config silently");

        var accessKey = dbScope.Db.SystemSettings
            .FirstOrDefault(s => s.Key == MinioSettings.KeyAccessKey);
        accessKey.Should().BeNull(
            "the later iterations of the loop must not persist on rollback");
    }

    [Fact]
    public async Task Handle_UnknownIntegrationProvider_ThrowsKeyNotFound()
    {
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out _);

        var act = () => handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "no-such-integration",
            Settings: new()),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_KeyNotOwnedByProvider_ThrowsAndRollsBack()
    {
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out _);

        var act = () => handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "minio",
            Settings: new Dictionary<string, string>
            {
                ["smtp.host"] = "smtp.example.com",  // foreign key for provider
            }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not part of integration*");

        dbScope.Db.SystemSettings.Should().BeEmpty(
            "cross-provider writes are refused without partial persist");
    }

    [Fact]
    public async Task Handle_Usps_PropagatesConsumerSecret_OnSave()
    {
        // Regression: pre-fix ApplyUsps mirrored KeyUserId into ConsumerKey
        // only. ConsumerSecret persisted to DB but the running singleton
        // kept the stale value until the next process restart — silently
        // breaking USPS OAuth client-credentials renewal until someone
        // bounced the API container.
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out _, out var usps, out _, out _);

        await handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "usps",
            Settings: new Dictionary<string, string>
            {
                [UspsSettings.KeyConsumerKey] = "test-key",
                [UspsSettings.KeyConsumerSecret] = "test-secret",
            }),
            CancellationToken.None);

        usps.ConsumerKey.Should().Be("test-key");
        usps.ConsumerSecret.Should().Be("test-secret",
            "ConsumerSecret must hot-reload on save like every other secret field");
    }

    [Fact]
    public async Task Handle_Ai_PropagatesDocsPath_OnSave()
    {
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out _, out _, out var ai, out _);

        await handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "ai",
            Settings: new Dictionary<string, string>
            {
                [AiSettings.KeyDocsPath] = "/mnt/forge-docs",
            }),
            CancellationToken.None);

        ai.DocsPath.Should().Be("/mnt/forge-docs",
            "DocsPath drives the Hangfire RAG-index job — admin re-pointing the docs " +
            "directory must take effect without restarting the API");
    }

    [Fact]
    public async Task Handle_GoogleDrive_PropagatesClientCredentials_OnSave()
    {
        // Drive moved off appsettings.json (per the "zero config-file
        // modification" rule); admin saves are now the canonical surface.
        // ApplyGoogleDrive must hot-reload ClientId / ClientSecret / Scopes
        // onto IOptions<GoogleDriveOptions> so the running service picks
        // them up without a restart.
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out _, out _, out _, out var gdrive);

        await handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "gdrive",
            Settings: new Dictionary<string, string>
            {
                [GoogleDriveSettings.KeyClientId] = "abc.apps.googleusercontent.com",
                [GoogleDriveSettings.KeyClientSecret] = "drive-secret",
                [GoogleDriveSettings.KeyScopes] = "https://www.googleapis.com/auth/drive",
            }),
            CancellationToken.None);

        gdrive.ClientId.Should().Be("abc.apps.googleusercontent.com");
        gdrive.ClientSecret.Should().Be("drive-secret");
        gdrive.Scopes.Should().Be("https://www.googleapis.com/auth/drive",
            "Scopes must round-trip so admin can switch drive.file ↔ drive without redeploying");
    }

    [Fact]
    public async Task Handle_QuickBooks_PropagatesClientCredentials_AndMapsModeToEnvironment()
    {
        // QB descriptor was theatrical pre-fix — saves persisted but the
        // running QuickBooksOptions kept appsettings.json values, so the
        // OAuth /authorize endpoint used the wrong ClientId.
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out _);
        var qb = new QuickBooksOptions();
        // Re-construct with a known QB options so we can assert against it.
        var dp = new EphemeralDataProtectionProvider();
        var ss = new SettingsService(dbScope.Db, dp);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetIntegrationSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationSettingsResult(true,
                IntegrationDescriptorCatalog.All
                    .Select(d => new IntegrationStatusModel(d.Provider, d.Name, d.Description, d.Icon, false, new(), d.Category))
                    .ToList()));
        var h = new UpdateIntegrationSettingsHandler(
            ss, dbScope.Db, mediator.Object,
            Options.Create(new SmtpOptions()), Options.Create(new MinioOptions()),
            Options.Create(new UspsOptions()), Options.Create(new DocuSealOptions()),
            Options.Create(new AiOptions()), Options.Create(new GoogleDriveOptions()),
            Options.Create(qb),
            Options.Create(new XeroOptions()), Options.Create(new FreshBooksOptions()),
            Options.Create(new SageOptions()), Options.Create(new NetSuiteOptions()),
            Options.Create(new WaveOptions()), Options.Create(new ZohoOptions()));

        await h.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "quickbooks",
            Settings: new Dictionary<string, string>
            {
                ["quickbooks.mode"] = "Real",
                ["quickbooks.client-id"] = "ABabc...",
                ["quickbooks.client-secret"] = "qb-shh",
            }),
            CancellationToken.None);

        qb.ClientId.Should().Be("ABabc...");
        qb.ClientSecret.Should().Be("qb-shh");
        qb.Environment.Should().Be("production",
            "Real mode must map to QuickBooksOptions.Environment = production " +
            "(sandbox is for Mock/Disabled)");
    }

    [Fact]
    public async Task Handle_Wave_PropagatesAccessTokenAndBusinessId_NotGenericClientIdSecret()
    {
        // Pre-fix descriptor wrongly exposed wave.client-id / wave.client-secret
        // (ProviderBlock template); WaveOptions has neither. The descriptor
        // now exposes wave.access-token + wave.business-id which actually
        // match the options model.
        using var dbScope = new AppDbContextLike();
        var dp = new EphemeralDataProtectionProvider();
        var ss = new SettingsService(dbScope.Db, dp);
        var wave = new WaveOptions();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetIntegrationSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationSettingsResult(true,
                IntegrationDescriptorCatalog.All
                    .Select(d => new IntegrationStatusModel(d.Provider, d.Name, d.Description, d.Icon, false, new(), d.Category))
                    .ToList()));
        var h = new UpdateIntegrationSettingsHandler(
            ss, dbScope.Db, mediator.Object,
            Options.Create(new SmtpOptions()), Options.Create(new MinioOptions()),
            Options.Create(new UspsOptions()), Options.Create(new DocuSealOptions()),
            Options.Create(new AiOptions()), Options.Create(new GoogleDriveOptions()),
            Options.Create(new QuickBooksOptions()),
            Options.Create(new XeroOptions()), Options.Create(new FreshBooksOptions()),
            Options.Create(new SageOptions()), Options.Create(new NetSuiteOptions()),
            Options.Create(wave), Options.Create(new ZohoOptions()));

        await h.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "wave",
            Settings: new Dictionary<string, string>
            {
                ["wave.access-token"] = "wv-token-xyz",
                ["wave.business-id"] = "biz_abc123",
            }),
            CancellationToken.None);

        wave.AccessToken.Should().Be("wv-token-xyz");
        wave.BusinessId.Should().Be("biz_abc123");
    }

    [Fact]
    public async Task Handle_MaskedSecretValue_IsSkipped_NotOverwriting()
    {
        // Pre-1m UI bug class: when the user saves without re-typing the
        // secret, the form sends the mask glyph back. Server must NOT
        // overwrite the stored value with the mask.
        using var dbScope = new AppDbContextLike();
        var handler = MakeHandler(dbScope, out _);

        // First save: real secret.
        await handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "minio",
            Settings: new Dictionary<string, string>
            {
                [MinioSettings.KeySecretKey] = "real-secret-value",
            }),
            CancellationToken.None);

        var rowAfterFirst = dbScope.Db.SystemSettings
            .First(s => s.Key == MinioSettings.KeySecretKey);
        var sealedAfterFirst = rowAfterFirst.Value;

        // Second save: mask glyph (eight bullets) — simulating UI submitting
        // unchanged secret-field display.
        await handler.Handle(new UpdateIntegrationSettingsCommand(
            Provider: "minio",
            Settings: new Dictionary<string, string>
            {
                [MinioSettings.KeySecretKey] = "••••••••",
            }),
            CancellationToken.None);

        var rowAfterSecond = dbScope.Db.SystemSettings
            .First(s => s.Key == MinioSettings.KeySecretKey);
        rowAfterSecond.Value.Should().Be(sealedAfterFirst,
            "masked-secret send must be a no-op — overwriting with the mask would corrupt the stored credential");
    }
}

/// <summary>
/// Wraps a TestAppDbContext + scope-management for tests that don't want
/// to manage the using lifetime manually. Disposing this disposes the
/// underlying context.
/// </summary>
internal sealed class AppDbContextLike : IDisposable
{
    public Forge.Data.Context.AppDbContext Db { get; } = TestDbContextFactory.Create();
    public void Dispose() => Db.Dispose();
}

/// <summary>
/// Test-only DataProtectionProvider — returns a no-op protector so secrets
/// "seal" to themselves. Lets us assert the stored Value remained stable
/// across writes without dragging real Data Protection into the unit-test
/// pipeline.
/// </summary>
internal sealed class EphemeralDataProtectionProvider : IDataProtectionProvider
{
    public IDataProtector CreateProtector(string purpose) => new NoopProtector();

    private sealed class NoopProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }
}
