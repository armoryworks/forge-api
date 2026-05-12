using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Pro Services rollout — real OneDrive provider via Microsoft Graph
/// API v1.0. Implements <see cref="ICloudStorageIntegrationService"/>.
///
/// <para>Graph endpoints:
/// <list type="bullet">
///   <item>POST /v1.0/me/drive/items/{parent-id}/children (or /root/children) — create folder</item>
///   <item>GET /v1.0/me/drive/items/{id} — get folder</item>
///   <item>GET /v1.0/me/drive/items/{id}/children?$filter=folder ne null — list child folders</item>
///   <item>GET /v1.0/me/drive/root:/{path} — native path lookup (no walking needed)</item>
/// </list></para>
///
/// <para>Unlike Google Drive, Graph supports native path-based addressing:
/// <c>/v1.0/me/drive/root:/Customers/ACME/Project-42</c>. <see cref="FindFolderByPathAsync"/>
/// uses this directly — much faster than the per-segment walk Drive requires.</para>
/// </summary>
public class OneDriveCloudStorageService : ICloudStorageIntegrationService
{
    private const string GraphApiBase = "https://graph.microsoft.com/v1.0";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OneDriveOptions _options;
    private readonly ILogger<OneDriveCloudStorageService> _logger;

    public OneDriveCloudStorageService(
        IHttpClientFactory httpClientFactory,
        IOptions<OneDriveOptions> options,
        ILogger<OneDriveCloudStorageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderCode => "onedrive";

    public async Task<CloudFolder> CreateFolderAsync(
        string accessToken,
        CreateFolderRequest request,
        CancellationToken ct)
    {
        var client = CreateClient(accessToken);
        var parentRef = string.IsNullOrEmpty(request.ParentExternalId) ? "root" : $"items/{request.ParentExternalId}";

        // Graph "create folder" semantics:
        //   conflictBehavior=replace → fail / replace existing
        //   conflictBehavior=rename  → auto-rename ("Foo 2", "Foo 3")
        //   conflictBehavior=fail    → 409
        // For EnsureExists we use "fail" then look up by path on conflict.
        var conflictBehavior = request.EnsureExists ? "fail" : "rename";
        var body = new
        {
            name = request.Name,
            folder = new { },
            @microsoft_graph_conflictBehavior = conflictBehavior,
        };

        // Manual JSON serialization to handle the "@microsoft.graph.conflictBehavior"
        // property name (the @ + dot syntax doesn't map cleanly to a C# property).
        var json = JsonSerializer.Serialize(new
        {
            name = request.Name,
            folder = new { },
        });
        var withConflict = json.Insert(json.Length - 1, $",\"@microsoft.graph.conflictBehavior\":\"{conflictBehavior}\"");
        var content = new StringContent(withConflict, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{GraphApiBase}/me/drive/{parentRef}/children", content, ct);

        // EnsureExists + 409 → fall back to path lookup.
        if (request.EnsureExists && response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogInformation("OneDrive: CreateFolder conflict on '{Name}' under '{ParentRef}'; resolving existing", request.Name, parentRef);
            var parentPath = await ResolveParentPath(accessToken, request.ParentExternalId, ct);
            var fullPath = parentPath == "/" ? $"/{request.Name}" : $"{parentPath}/{request.Name}";
            var existing = await FindFolderByPathAsync(accessToken, fullPath, ct);
            if (existing is not null) return existing;
        }

        response.EnsureSuccessStatusCode();
        var resultJson = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var folder = ParseFolder(resultJson);
        _logger.LogInformation("OneDrive: Created folder '{Name}' (id={Id})", folder.Name, folder.ExternalId);
        return folder;
    }

    public async Task<CloudFolder?> GetFolderAsync(
        string accessToken,
        string folderExternalId,
        CancellationToken ct)
    {
        var client = CreateClient(accessToken);
        var response = await client.GetAsync(
            $"{GraphApiBase}/me/drive/items/{Uri.EscapeDataString(folderExternalId)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ParseFolder(json);
    }

    public async Task<IReadOnlyList<CloudFolder>> ListChildFoldersAsync(
        string accessToken,
        string parentExternalId,
        CancellationToken ct)
    {
        var client = CreateClient(accessToken);
        // $filter=folder ne null → folders only.
        var response = await client.GetAsync(
            $"{GraphApiBase}/me/drive/items/{Uri.EscapeDataString(parentExternalId)}/children?$filter=folder%20ne%20null&$top=200",
            ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!json.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray().Select(ParseFolder).ToList();
    }

    public async Task<CloudFolder?> FindFolderByPathAsync(
        string accessToken,
        string path,
        CancellationToken ct)
    {
        // Graph supports native path lookup: /me/drive/root:/{path}
        // Trim leading slash; segments must be URL-encoded individually.
        var trimmed = (path ?? string.Empty).Trim('/');
        if (string.IsNullOrEmpty(trimmed)) return null;

        var encoded = string.Join(
            "/",
            trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        var client = CreateClient(accessToken);
        var response = await client.GetAsync($"{GraphApiBase}/me/drive/root:/{encoded}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        // Only return if it's actually a folder.
        if (!json.TryGetProperty("folder", out _)) return null;
        return ParseFolder(json);
    }

    public async Task<CloudStorageTokenRefreshResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken ct)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("OneDrive credentials are not configured.");

        var client = _httpClientFactory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = _options.Scopes,
        });
        var tenant = string.IsNullOrEmpty(_options.TenantId) ? "common" : _options.TenantId;
        var response = await client.PostAsync(
            $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token", form, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var access = json.GetProperty("access_token").GetString()!;
        var newRefresh = json.TryGetProperty("refresh_token", out var rt)
            ? rt.GetString() ?? refreshToken
            : refreshToken;
        var expiresIn = json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        return new CloudStorageTokenRefreshResult(access, newRefresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    public async Task<CloudStorageHealthResult> HealthCheckAsync(
        string accessToken,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = CreateClient(accessToken);
            var response = await client.GetAsync($"{GraphApiBase}/me/drive?$select=id,driveType", ct);
            sw.Stop();
            response.EnsureSuccessStatusCode();
            return new CloudStorageHealthResult(true, ProviderCode, "OK", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "OneDrive health check failed");
            return new CloudStorageHealthResult(false, ProviderCode, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private async Task<string> ResolveParentPath(string accessToken, string? parentExternalId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(parentExternalId)) return "/";
        var parent = await GetFolderAsync(accessToken, parentExternalId, ct);
        return parent?.Path ?? "/";
    }

    private HttpClient CreateClient(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static CloudFolder ParseFolder(JsonElement json)
    {
        var id = json.GetProperty("id").GetString()!;
        var name = json.GetProperty("name").GetString() ?? string.Empty;
        var webUrl = json.TryGetProperty("webUrl", out var w)
            ? w.GetString() ?? string.Empty
            : string.Empty;
        // Graph returns parentReference.path = "/drive/root:/Customers" — strip
        // the "/drive/root:" prefix to get the human path. Append name.
        var parentPath = "/";
        string? parentId = null;
        if (json.TryGetProperty("parentReference", out var pref))
        {
            if (pref.TryGetProperty("path", out var pPath) && pPath.GetString() is { } rawPath)
            {
                var idx = rawPath.IndexOf("root:", StringComparison.Ordinal);
                parentPath = idx >= 0 ? rawPath[(idx + 5)..] : rawPath;
                if (string.IsNullOrEmpty(parentPath)) parentPath = "/";
            }
            if (pref.TryGetProperty("id", out var pId))
            {
                parentId = pId.GetString();
            }
        }
        var path = parentPath == "/" ? $"/{name}" : $"{parentPath}/{name}";
        return new CloudFolder(id, name, path, webUrl, parentId);
    }
}
