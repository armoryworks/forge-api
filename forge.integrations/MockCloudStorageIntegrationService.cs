using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Pro Services rollout — in-memory mock cloud-storage provider. Used when
/// <c>MockIntegrations=true</c> (Development) or when no real provider
/// credentials are configured. Stores folders in a thread-safe dictionary
/// keyed by provider-side external id; folder URLs and paths are
/// synthesized.
///
/// <para>Behaves like a single provider — <see cref="ProviderCode"/>
/// returns <c>"mock"</c>. Tests / dev exercises pick the mock; production
/// installs swap in the real Google Drive / OneDrive / Dropbox services
/// per the DI registration in Program.cs.</para>
/// </summary>
public class MockCloudStorageIntegrationService : ICloudStorageIntegrationService
{
    private readonly ILogger<MockCloudStorageIntegrationService> _logger;
    private readonly ConcurrentDictionary<string, CloudFolder> _folders = new();
    private long _nextId;

    public MockCloudStorageIntegrationService(ILogger<MockCloudStorageIntegrationService> logger)
    {
        _logger = logger;
        // Pre-seed a root folder so child-of-root operations resolve cleanly.
        const string rootId = "mock-root";
        _folders.TryAdd(rootId, new CloudFolder(
            ExternalId: rootId,
            Name: "Root",
            Path: "/",
            WebUrl: "https://mock-cloud.local/",
            ParentExternalId: null));
    }

    public string ProviderCode => "mock";

    public Task<CloudFolder> CreateFolderAsync(
        string accessToken,
        CreateFolderRequest request,
        CancellationToken ct)
    {
        var parentPath = ResolveParentPath(request.ParentExternalId);
        var path = parentPath == "/" ? $"/{request.Name}" : $"{parentPath}/{request.Name}";

        // EnsureExists: return the existing folder if one already lives at this path.
        if (request.EnsureExists)
        {
            var existing = _folders.Values.FirstOrDefault(f => f.Path == path);
            if (existing is not null)
            {
                _logger.LogInformation("[MockCloud] CreateFolder ensure-exists hit '{Path}'", path);
                return Task.FromResult(existing);
            }
        }

        var newId = $"mock-folder-{Interlocked.Increment(ref _nextId)}";
        var folder = new CloudFolder(
            ExternalId: newId,
            Name: request.Name,
            Path: path,
            WebUrl: $"https://mock-cloud.local{path}",
            ParentExternalId: request.ParentExternalId);
        _folders.TryAdd(newId, folder);
        _logger.LogInformation("[MockCloud] CreateFolder '{Path}' (id={Id})", path, newId);
        return Task.FromResult(folder);
    }

    public Task<CloudFolder?> GetFolderAsync(
        string accessToken,
        string folderExternalId,
        CancellationToken ct)
    {
        _folders.TryGetValue(folderExternalId, out var folder);
        return Task.FromResult(folder);
    }

    public Task<IReadOnlyList<CloudFolder>> ListChildFoldersAsync(
        string accessToken,
        string parentExternalId,
        CancellationToken ct)
    {
        IReadOnlyList<CloudFolder> children = _folders.Values
            .Where(f => f.ParentExternalId == parentExternalId)
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(children);
    }

    public Task<CloudFolder?> FindFolderByPathAsync(
        string accessToken,
        string path,
        CancellationToken ct)
    {
        var folder = _folders.Values.FirstOrDefault(f => f.Path == path);
        return Task.FromResult(folder);
    }

    public Task<CloudStorageTokenRefreshResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken ct)
    {
        _logger.LogInformation("[MockCloud] RefreshToken (mock returns same token, +1 hour)");
        return Task.FromResult(new CloudStorageTokenRefreshResult(
            AccessToken: $"mock-access-{Guid.NewGuid():N}",
            RefreshToken: refreshToken,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1)));
    }

    public Task<CloudStorageHealthResult> HealthCheckAsync(
        string accessToken,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        sw.Stop();
        return Task.FromResult(new CloudStorageHealthResult(
            IsHealthy: true,
            ProviderCode: ProviderCode,
            Message: "Mock provider — always healthy.",
            LatencyMs: sw.ElapsedMilliseconds));
    }

    private string ResolveParentPath(string? parentExternalId)
    {
        if (string.IsNullOrEmpty(parentExternalId)) return "/";
        return _folders.TryGetValue(parentExternalId, out var parent) ? parent.Path : "/";
    }
}
