using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Pro Services rollout — real Dropbox provider via Dropbox API v2.
/// Implements <see cref="ICloudStorageIntegrationService"/>.
///
/// <para>Dropbox's data model: paths ARE the canonical addressing. Each
/// folder has a path (case-insensitive) and an opaque <c>id:abc123</c>
/// token; this service uses the id as <c>ExternalId</c> and the path as
/// <c>Path</c>. Both work as inputs to most endpoints, but ids are
/// stable across renames.</para>
///
/// <para>Endpoints used:
/// <list type="bullet">
///   <item>POST /2/files/create_folder_v2 — create folder</item>
///   <item>POST /2/files/get_metadata — get folder by id or path</item>
///   <item>POST /2/files/list_folder — list children</item>
///   <item>POST /2/files/list_folder/continue — paginated continuation</item>
///   <item>POST /2/users/get_current_account — health check</item>
///   <item>POST api.dropbox.com/oauth2/token — refresh access token</item>
/// </list></para>
/// </summary>
public class DropboxCloudStorageService : ICloudStorageIntegrationService
{
    private const string ApiBase = "https://api.dropboxapi.com/2";
    private const string AuthBase = "https://api.dropbox.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DropboxOptions _options;
    private readonly ILogger<DropboxCloudStorageService> _logger;

    public DropboxCloudStorageService(
        IHttpClientFactory httpClientFactory,
        IOptions<DropboxOptions> options,
        ILogger<DropboxCloudStorageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderCode => "dropbox";

    public async Task<CloudFolder> CreateFolderAsync(
        string accessToken,
        CreateFolderRequest request,
        CancellationToken ct)
    {
        // Dropbox uses paths exclusively for folder creation. Resolve the
        // parent path; combine with the new folder name.
        var parentPath = await ResolveParentPathAsync(accessToken, request.ParentExternalId, ct);
        var fullPath = parentPath == "/" ? $"/{request.Name}" : $"{parentPath}/{request.Name}";

        if (request.EnsureExists)
        {
            // Probe the path first; return existing folder if present.
            var existing = await FindFolderByPathAsync(accessToken, fullPath, ct);
            if (existing is not null)
            {
                _logger.LogInformation("Dropbox: CreateFolder ensure-exists hit '{Path}'", fullPath);
                return existing;
            }
        }

        var client = CreateClient(accessToken);
        var body = new { path = fullPath, autorename = !request.EnsureExists };
        var response = await client.PostAsync($"{ApiBase}/files/create_folder_v2",
            JsonContent.Create(body), ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var metadata = json.GetProperty("metadata");
        var folder = ParseFolderMetadata(metadata);
        _logger.LogInformation("Dropbox: Created folder '{Path}' (id={Id})", folder.Path, folder.ExternalId);
        return folder;
    }

    public async Task<CloudFolder?> GetFolderAsync(
        string accessToken,
        string folderExternalId,
        CancellationToken ct)
    {
        var client = CreateClient(accessToken);
        // Dropbox's get_metadata accepts either an id (id:xxx) or a path.
        var body = new { path = folderExternalId };
        var response = await client.PostAsync($"{ApiBase}/files/get_metadata", JsonContent.Create(body), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict ||
            response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
        {
            // Dropbox returns 409 with body for "not found" — treat as null.
            var errBody = await response.Content.ReadAsStringAsync(ct);
            if (errBody.Contains("not_found", StringComparison.OrdinalIgnoreCase)) return null;
            response.EnsureSuccessStatusCode();
        }
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        // get_metadata returns metadata directly (no wrapper).
        if (!IsFolder(json)) return null;
        return ParseFolderMetadata(json);
    }

    public async Task<IReadOnlyList<CloudFolder>> ListChildFoldersAsync(
        string accessToken,
        string parentExternalId,
        CancellationToken ct)
    {
        var folders = new List<CloudFolder>();
        var client = CreateClient(accessToken);
        var body = new { path = parentExternalId, recursive = false, limit = 200 };
        var response = await client.PostAsync($"{ApiBase}/files/list_folder", JsonContent.Create(body), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict) return [];
        response.EnsureSuccessStatusCode();
        await CollectFoldersAsync(response, folders, ct);

        // Pagination — Dropbox's list_folder returns has_more + cursor.
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(ct));
        while (json.TryGetProperty("has_more", out var more) && more.GetBoolean())
        {
            var cursor = json.GetProperty("cursor").GetString()!;
            var nextResp = await client.PostAsync(
                $"{ApiBase}/files/list_folder/continue",
                JsonContent.Create(new { cursor }), ct);
            nextResp.EnsureSuccessStatusCode();
            await CollectFoldersAsync(nextResp, folders, ct);
            json = JsonSerializer.Deserialize<JsonElement>(await nextResp.Content.ReadAsStringAsync(ct));
        }
        return folders;
    }

    public async Task<CloudFolder?> FindFolderByPathAsync(
        string accessToken,
        string path,
        CancellationToken ct)
    {
        var normalized = (path ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized) || normalized == "/") return null;
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        return await GetFolderAsync(accessToken, normalized, ct);
    }

    public async Task<CloudStorageTokenRefreshResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken ct)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("Dropbox credentials are not configured.");

        var client = _httpClientFactory.CreateClient();
        // Dropbox auth uses Basic auth on the token endpoint.
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AppKey}:{_options.AppSecret}"));
        var req = new HttpRequestMessage(HttpMethod.Post, $"{AuthBase}/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
        var response = await client.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var access = json.GetProperty("access_token").GetString()!;
        // Dropbox does not rotate refresh tokens — the same one stays valid.
        var expiresIn = json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 14400;
        return new CloudStorageTokenRefreshResult(access, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    public async Task<CloudStorageHealthResult> HealthCheckAsync(
        string accessToken,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = CreateClient(accessToken);
            // get_current_account is the canonical "am I authed?" probe.
            var response = await client.PostAsync($"{ApiBase}/users/get_current_account",
                new StringContent("null", Encoding.UTF8, "application/json"), ct);
            sw.Stop();
            response.EnsureSuccessStatusCode();
            return new CloudStorageHealthResult(true, ProviderCode, "OK", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Dropbox health check failed");
            return new CloudStorageHealthResult(false, ProviderCode, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private async Task<string> ResolveParentPathAsync(string accessToken, string? parentExternalId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(parentExternalId)) return string.Empty;  // Dropbox root is "" (not "/")
        if (parentExternalId.StartsWith('/')) return parentExternalId;
        var parent = await GetFolderAsync(accessToken, parentExternalId, ct);
        return parent?.Path ?? string.Empty;
    }

    private async Task CollectFoldersAsync(HttpResponseMessage response, List<CloudFolder> sink, CancellationToken ct)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!json.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return;
        foreach (var entry in entries.EnumerateArray())
        {
            if (IsFolder(entry)) sink.Add(ParseFolderMetadata(entry));
        }
    }

    private HttpClient CreateClient(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static bool IsFolder(JsonElement json)
    {
        return json.TryGetProperty(".tag", out var tag) &&
               string.Equals(tag.GetString(), "folder", StringComparison.OrdinalIgnoreCase);
    }

    private static CloudFolder ParseFolderMetadata(JsonElement json)
    {
        var id = json.GetProperty("id").GetString()!;
        var name = json.GetProperty("name").GetString() ?? string.Empty;
        var path = json.TryGetProperty("path_display", out var pd) ? pd.GetString() ?? string.Empty : string.Empty;
        // Dropbox doesn't return a webUrl on metadata — compose one to the
        // standard Dropbox "Home" surface.
        var webUrl = $"https://www.dropbox.com/home{path}";
        // Dropbox returns parent only via path inspection.
        var parentPath = string.IsNullOrEmpty(path) ? string.Empty
            : path.Substring(0, Math.Max(0, path.LastIndexOf('/')));
        // No native parent id; encode the parent path so callers can detect siblings.
        var parentToken = string.IsNullOrEmpty(parentPath) ? null : parentPath;
        return new CloudFolder(id, name, path, webUrl, parentToken);
    }
}
