using System.Text.Json;

using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Api.Features.CloudStorage;
using Forge.Api.Features.Presets.Apply.Layers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CloudStorage;

public class CloudStorageAdminTests
{
    private const string Umbrella = "CAP-EXT-CLOUD-STORAGE";
    private const string Gdrive = "CAP-EXT-CLOUD-STORAGE-GDRIVE";

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly ITokenEncryptionService _encryption = new ReversingEncryption();

    public CloudStorageAdminTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    }

    private CreateCloudStorageProviderHandler Create(params string[] caps) =>
        new(_db, _encryption, new StubCapabilitySnapshotProvider(caps));

    private static CreateCloudStorageProviderRequestModel ServiceAccount(string code = "gdrive") =>
        new(code, CloudStorageProviderMode.ServiceAccount, "root-123", "svc@example.test", "access-1", "refresh-1", null);

    [Fact]
    public async Task A_service_account_provider_stores_its_tokens_encrypted_and_never_returns_them()
    {
        var result = await Create(Umbrella, Gdrive).Handle(new CreateCloudStorageProviderCommand(ServiceAccount(" GDrive ")), CancellationToken.None);

        result.ProviderCode.Should().Be("gdrive");
        result.CredentialsConfigured.Should().BeTrue();
        var row = _db.CloudStorageProviders.Single();
        row.OAuthTokenEncrypted.Should().Be(_encryption.Encrypt("access-1"));
        row.RefreshTokenEncrypted.Should().Be(_encryption.Encrypt("refresh-1"));
        typeof(CloudStorageProviderResponseModel).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Token") && n != nameof(CloudStorageProviderResponseModel.TokenExpiresAt));
    }

    [Fact]
    public async Task A_provider_whose_capability_is_off_is_refused()
    {
        var act = () => Create(Umbrella).Handle(new CreateCloudStorageProviderCommand(ServiceAccount()), CancellationToken.None);

        (await act.Should().ThrowAsync<CapabilityDisabledException>()).Which.Capability.Should().Be(Gdrive);
        _db.CloudStorageProviders.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_one_active_provider_of_a_kind_is_allowed()
    {
        await Create(Umbrella, Gdrive).Handle(new CreateCloudStorageProviderCommand(ServiceAccount()), CancellationToken.None);

        var act = () => Create(Umbrella, Gdrive).Handle(new CreateCloudStorageProviderCommand(ServiceAccount()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Theory]
    [InlineData("box", CloudStorageProviderMode.PerUser, null, null)]
    [InlineData("gdrive", CloudStorageProviderMode.ServiceAccount, null, "refresh")]
    [InlineData("gdrive", CloudStorageProviderMode.ServiceAccount, "access", null)]
    public void Creates_are_validated(string code, CloudStorageProviderMode mode, string? access, string? refresh)
    {
        var result = new CreateCloudStorageProviderValidator().TestValidate(new CreateCloudStorageProviderCommand(
            new CreateCloudStorageProviderRequestModel(code, mode, null, null, access, refresh, null)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_per_user_provider_needs_no_install_tokens()
    {
        var result = new CreateCloudStorageProviderValidator().TestValidate(new CreateCloudStorageProviderCommand(
            new CreateCloudStorageProviderRequestModel("dropbox", CloudStorageProviderMode.PerUser, "/Forge", null, null, null, null)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Editing_keeps_the_stored_tokens_unless_new_ones_are_entered()
    {
        var created = await Create(Umbrella, Gdrive).Handle(new CreateCloudStorageProviderCommand(ServiceAccount()), CancellationToken.None);
        var handler = new UpdateCloudStorageProviderHandler(_db, _encryption, new StubCapabilitySnapshotProvider(Umbrella, Gdrive));

        var result = await handler.Handle(new UpdateCloudStorageProviderCommand(created.Id,
            new UpdateCloudStorageProviderRequestModel(CloudStorageProviderMode.ServiceAccount, "root-456", "svc@example.test", null, null, null, true)),
            CancellationToken.None);

        result.RootFolderId.Should().Be("root-456");
        _db.CloudStorageProviders.Single().OAuthTokenEncrypted.Should().Be(_encryption.Encrypt("access-1"));
    }

    [Fact]
    public async Task A_provider_with_linked_records_cannot_be_deleted()
    {
        var created = await Create(Umbrella, Gdrive).Handle(new CreateCloudStorageProviderCommand(ServiceAccount()), CancellationToken.None);
        _db.EntityCloudLinks.Add(new EntityCloudLink { EntityType = "Job", EntityId = 1, ProviderId = created.Id, FolderExternalId = "f1" });
        _db.SaveChanges();

        var act = () => new DeleteCloudStorageProviderHandler(_db, _clock.Object)
            .Handle(new DeleteCloudStorageProviderCommand(created.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Deactivate*");
    }

    [Fact]
    public async Task An_unlinked_provider_is_soft_deleted()
    {
        var created = await Create(Umbrella, Gdrive).Handle(new CreateCloudStorageProviderCommand(ServiceAccount()), CancellationToken.None);

        await new DeleteCloudStorageProviderHandler(_db, _clock.Object)
            .Handle(new DeleteCloudStorageProviderCommand(created.Id), CancellationToken.None);

        _db.CloudStorageProviders.Should().BeEmpty();
    }

    [Fact]
    public async Task Test_reports_the_provider_health_check_and_stamps_the_connection()
    {
        var created = await Create(Umbrella, Gdrive).Handle(new CreateCloudStorageProviderCommand(ServiceAccount()), CancellationToken.None);
        var service = new Mock<ICloudStorageIntegrationService>();
        service.Setup(s => s.HealthCheckAsync("live-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CloudStorageHealthResult(true, "gdrive", "Connected as svc@example.test", 42));
        var resolver = new Mock<ICloudStorageResolver>();
        resolver.Setup(r => r.ResolveByCode("gdrive")).Returns(service.Object);
        var tokens = new Mock<ICloudStorageTokenManager>();
        tokens.Setup(t => t.GetValidAccessTokenAsync(It.IsAny<CloudStorageProvider>(), It.IsAny<CancellationToken>())).ReturnsAsync("live-token");

        var result = await new TestCloudStorageProviderHandler(_db, resolver.Object, tokens.Object, _clock.Object)
            .Handle(new TestCloudStorageProviderCommand(created.Id, 1), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.LatencyMs.Should().Be(42);
        _db.CloudStorageProviders.Single().LastConnectedAt.Should().Be(_clock.Object.UtcNow);
    }

    [Fact]
    public async Task Test_fails_with_a_reason_when_the_connector_is_not_configured()
    {
        var created = await Create(Umbrella, Gdrive).Handle(new CreateCloudStorageProviderCommand(ServiceAccount()), CancellationToken.None);

        var result = await new TestCloudStorageProviderHandler(_db, Mock.Of<ICloudStorageResolver>(), Mock.Of<ICloudStorageTokenManager>(), _clock.Object)
            .Handle(new TestCloudStorageProviderCommand(created.Id, 1), CancellationToken.None);

        result.Success.Should().BeFalse();
        _db.CloudStorageProviders.Single().LastError.Should().Be(result.Message);
    }

    [Fact]
    public async Task Provider_types_report_capability_and_registration()
    {
        var resolver = new Mock<ICloudStorageResolver>();
        resolver.SetupGet(r => r.RegisteredProviderCodes).Returns(new HashSet<string> { "mock", "gdrive" });

        var types = await new GetCloudStorageProviderTypesHandler(new StubCapabilitySnapshotProvider(Umbrella, Gdrive), resolver.Object)
            .Handle(new GetCloudStorageProviderTypesQuery(), CancellationToken.None);

        types.Single(t => t.Code == "gdrive").Should().Be(new CloudStorageProviderTypeModel("gdrive", Gdrive, true, true));
        types.Single(t => t.Code == "dropbox").CapabilityEnabled.Should().BeFalse();
        types.Single(t => t.Code == "mock").CapabilityEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task The_folder_map_is_saved_in_the_shape_the_folder_auto_creator_reads()
    {
        var entries = new List<FolderMapSuggestion>
        {
            new("customer", "Clients/{Customer}", ["Quotes", "Drawings"]),
            new("Job", "Clients/{Customer}/{Year}/{Job}", [], AutoCreateOnEntityCreate: false),
        };

        await new UpdateCloudStorageFolderMapHandler(_db).Handle(new UpdateCloudStorageFolderMapCommand(entries), CancellationToken.None);

        var raw = _db.SystemSettings.Single(s => s.Key == FolderMapBundleApplier.FolderMapSettingKey).Value;
        var stored = JsonSerializer.Deserialize<List<FolderMapSuggestion>>(raw)!;
        stored.Select(s => s.EntityType).Should().Equal("Customer", "Job");
        stored[0].SubfolderNames.Should().Equal("Quotes", "Drawings");
        stored[1].AutoCreateOnEntityCreate.Should().BeFalse();

        var readBack = await new GetCloudStorageFolderMapHandler(_db).Handle(new GetCloudStorageFolderMapQuery(), CancellationToken.None);
        readBack.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Vendor", "Vendors/{Vendor}", "folder-map-entity-unknown")]
    [InlineData("Customer", "Clients/{Client}", "folder-map-token-unknown")]
    public void Folder_map_entries_must_name_a_supported_record_and_known_tokens(string entityType, string template, string code)
    {
        var result = new UpdateCloudStorageFolderMapValidator().TestValidate(
            new UpdateCloudStorageFolderMapCommand([new FolderMapSuggestion(entityType, template, [])]));

        result.Errors.Should().Contain(e => e.ErrorCode == code);
    }

    [Fact]
    public void A_record_type_may_appear_only_once()
    {
        var result = new UpdateCloudStorageFolderMapValidator().TestValidate(new UpdateCloudStorageFolderMapCommand(
            [new FolderMapSuggestion("Job", "{Job}", []), new FolderMapSuggestion("job", "{Year}/{Job}", [])]));

        result.Errors.Should().Contain(e => e.ErrorCode == "folder-map-duplicate-entity");
    }

    [Fact]
    public void Subfolder_names_cannot_split_the_path()
    {
        var result = new UpdateCloudStorageFolderMapValidator().TestValidate(new UpdateCloudStorageFolderMapCommand(
            [new FolderMapSuggestion("Job", "{Job}", ["CAD/Old"])]));

        result.IsValid.Should().BeFalse();
    }

    private sealed class ReversingEncryption : ITokenEncryptionService
    {
        public string Encrypt(string plainText) => "enc:" + new string(plainText.Reverse().ToArray());
        public string Decrypt(string cipherText) => new(cipherText[4..].Reverse().ToArray());
    }
}
