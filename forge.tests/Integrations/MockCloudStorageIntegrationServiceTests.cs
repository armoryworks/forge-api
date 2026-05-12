using Microsoft.Extensions.Logging.Abstractions;

using Forge.Core.Models;
using Forge.Integrations;

namespace Forge.Tests.Integrations;

/// <summary>
/// Pro Services rollout — smoke tests for the in-memory cloud-storage mock.
/// Verifies the basic folder-CRUD behavior we'll lean on for the
/// FolderMapBundle applier and the dual-path auto-create flow (per D2).
/// </summary>
public class MockCloudStorageIntegrationServiceTests
{
    private static MockCloudStorageIntegrationService NewService() =>
        new(NullLogger<MockCloudStorageIntegrationService>.Instance);

    [Fact]
    public async Task ProviderCode_Reports_Mock()
    {
        var service = NewService();
        Assert.Equal("mock", service.ProviderCode);
    }

    [Fact]
    public async Task CreateFolder_At_Root_Returns_Folder_With_Url_And_Path()
    {
        var service = NewService();
        var folder = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest(Name: "Customers", ParentExternalId: null),
            CancellationToken.None);

        Assert.Equal("Customers", folder.Name);
        Assert.Equal("/Customers", folder.Path);
        Assert.NotNull(folder.WebUrl);
        Assert.Null(folder.ParentExternalId);
    }

    [Fact]
    public async Task CreateFolder_Nested_Resolves_Parent_Path()
    {
        var service = NewService();
        var parent = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest(Name: "ACME", ParentExternalId: null),
            CancellationToken.None);

        var child = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest(Name: "Project-42", ParentExternalId: parent.ExternalId),
            CancellationToken.None);

        Assert.Equal("/ACME/Project-42", child.Path);
        Assert.Equal(parent.ExternalId, child.ParentExternalId);
    }

    [Fact]
    public async Task CreateFolder_EnsureExists_Returns_Existing()
    {
        var service = NewService();
        var first = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest(Name: "Reports", ParentExternalId: null, EnsureExists: true),
            CancellationToken.None);

        var second = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest(Name: "Reports", ParentExternalId: null, EnsureExists: true),
            CancellationToken.None);

        Assert.Equal(first.ExternalId, second.ExternalId);
    }

    [Fact]
    public async Task FindFolderByPath_Resolves_Created_Folder()
    {
        var service = NewService();
        var created = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest(Name: "Engagements", ParentExternalId: null),
            CancellationToken.None);

        var found = await service.FindFolderByPathAsync("ignored", "/Engagements", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(created.ExternalId, found!.ExternalId);
    }

    [Fact]
    public async Task FindFolderByPath_Returns_Null_For_Missing_Path()
    {
        var service = NewService();
        var found = await service.FindFolderByPathAsync("ignored", "/Nonexistent", CancellationToken.None);
        Assert.Null(found);
    }

    [Fact]
    public async Task ListChildFolders_Returns_Direct_Children_Only()
    {
        var service = NewService();
        var parent = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest("ACME", null),
            CancellationToken.None);
        var childA = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest("Proposal", parent.ExternalId),
            CancellationToken.None);
        var childB = await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest("Deliverables", parent.ExternalId),
            CancellationToken.None);
        // Grandchild — should NOT appear in parent's child list.
        await service.CreateFolderAsync(
            accessToken: "ignored",
            request: new CreateFolderRequest("Sprint-1", childA.ExternalId),
            CancellationToken.None);

        var children = await service.ListChildFoldersAsync("ignored", parent.ExternalId, CancellationToken.None);
        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "Proposal");
        Assert.Contains(children, c => c.Name == "Deliverables");
    }

    [Fact]
    public async Task HealthCheck_Reports_Healthy()
    {
        var service = NewService();
        var result = await service.HealthCheckAsync("ignored", CancellationToken.None);
        Assert.True(result.IsHealthy);
        Assert.Equal("mock", result.ProviderCode);
    }

    [Fact]
    public async Task RefreshToken_Returns_New_Access_Token()
    {
        var service = NewService();
        var result = await service.RefreshTokenAsync("old-refresh", CancellationToken.None);
        Assert.NotNull(result.AccessToken);
        Assert.NotEmpty(result.AccessToken);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);
    }
}
