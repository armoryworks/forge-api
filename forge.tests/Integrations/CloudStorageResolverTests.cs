using Microsoft.Extensions.Logging.Abstractions;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;

namespace Forge.Tests.Integrations;

/// <summary>
/// Pro Services rollout — tests for the resolver that picks the right
/// cloud-storage provider by code (or returns a default). Uses real-ish
/// fake provider impls to avoid pulling in the full real-HTTP services.
/// </summary>
public class CloudStorageResolverTests
{
    [Fact]
    public void ResolveByCode_Returns_Mock_When_Only_Mock_Registered()
    {
        var mock = new MockCloudStorageIntegrationService(
            NullLogger<MockCloudStorageIntegrationService>.Instance);
        var resolver = new CloudStorageResolver(
            new ICloudStorageIntegrationService[] { mock },
            NullLogger<CloudStorageResolver>.Instance);

        Assert.NotNull(resolver.ResolveByCode("mock"));
        Assert.Null(resolver.ResolveByCode("gdrive"));
    }

    [Fact]
    public void ResolveByCode_Picks_Real_Provider_By_Code()
    {
        var mock = new MockCloudStorageIntegrationService(
            NullLogger<MockCloudStorageIntegrationService>.Instance);
        var fakeDrive = new FakeProvider("gdrive");
        var fakeOneDrive = new FakeProvider("onedrive");

        var resolver = new CloudStorageResolver(
            new ICloudStorageIntegrationService[] { mock, fakeDrive, fakeOneDrive },
            NullLogger<CloudStorageResolver>.Instance);

        Assert.Same(fakeDrive, resolver.ResolveByCode("gdrive"));
        Assert.Same(fakeOneDrive, resolver.ResolveByCode("onedrive"));
        Assert.Same(mock, resolver.ResolveByCode("mock"));
    }

    [Fact]
    public void ResolveByCode_Is_Case_Insensitive()
    {
        var mock = new MockCloudStorageIntegrationService(
            NullLogger<MockCloudStorageIntegrationService>.Instance);
        var resolver = new CloudStorageResolver(
            new ICloudStorageIntegrationService[] { mock },
            NullLogger<CloudStorageResolver>.Instance);

        Assert.NotNull(resolver.ResolveByCode("Mock"));
        Assert.NotNull(resolver.ResolveByCode("MOCK"));
    }

    [Fact]
    public void ResolveDefault_Returns_Mock_When_Only_Mock_Registered()
    {
        var mock = new MockCloudStorageIntegrationService(
            NullLogger<MockCloudStorageIntegrationService>.Instance);
        var resolver = new CloudStorageResolver(
            new ICloudStorageIntegrationService[] { mock },
            NullLogger<CloudStorageResolver>.Instance);

        Assert.Equal("mock", resolver.ResolveDefault().ProviderCode);
    }

    [Fact]
    public void ResolveDefault_Prefers_Real_Provider_Over_Mock()
    {
        var mock = new MockCloudStorageIntegrationService(
            NullLogger<MockCloudStorageIntegrationService>.Instance);
        var fakeDrive = new FakeProvider("gdrive");

        var resolver = new CloudStorageResolver(
            new ICloudStorageIntegrationService[] { mock, fakeDrive },
            NullLogger<CloudStorageResolver>.Instance);

        Assert.Equal("gdrive", resolver.ResolveDefault().ProviderCode);
    }

    [Fact]
    public void RegisteredProviderCodes_Reports_All_Codes()
    {
        var mock = new MockCloudStorageIntegrationService(
            NullLogger<MockCloudStorageIntegrationService>.Instance);
        var resolver = new CloudStorageResolver(
            new ICloudStorageIntegrationService[]
            {
                mock,
                new FakeProvider("gdrive"),
                new FakeProvider("dropbox"),
            },
            NullLogger<CloudStorageResolver>.Instance);

        Assert.Equal(3, resolver.RegisteredProviderCodes.Count);
        Assert.Contains("mock", resolver.RegisteredProviderCodes);
        Assert.Contains("gdrive", resolver.RegisteredProviderCodes);
        Assert.Contains("dropbox", resolver.RegisteredProviderCodes);
    }

    /// <summary>Throwaway test-only impl — does no real HTTP.</summary>
    private sealed class FakeProvider(string code) : ICloudStorageIntegrationService
    {
        public string ProviderCode { get; } = code;
        public Task<CloudFolder> CreateFolderAsync(string a, CreateFolderRequest r, CancellationToken ct) => throw new NotImplementedException();
        public Task<CloudFolder?> GetFolderAsync(string a, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<CloudFolder>> ListChildFoldersAsync(string a, string p, CancellationToken ct) => throw new NotImplementedException();
        public Task<CloudFolder?> FindFolderByPathAsync(string a, string p, CancellationToken ct) => throw new NotImplementedException();
        public Task<CloudStorageTokenRefreshResult> RefreshTokenAsync(string r, CancellationToken ct) => throw new NotImplementedException();
        public Task<CloudStorageHealthResult> HealthCheckAsync(string a, CancellationToken ct) => throw new NotImplementedException();
    }
}
