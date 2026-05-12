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
/// Pro Services rollout — real Google Drive provider for cloud-storage
/// folder operations. Implements <see cref="ICloudStorageIntegrationService"/>
/// using the Drive API v3 (REST + JSON).
///
/// <para>OAuth tokens flow in via parameters — this service does not
/// manage encryption/persistence. Callers retrieve the encrypted token
/// from CloudStorageProvider / UserCloudStorageLink, decrypt via
/// ITokenEncryptionService, pass here. On a 401, callers invoke
/// <see cref="RefreshTokenAsync"/> and retry.</para>
///
/// <para>Folder ID convention: Drive returns opaque file IDs
/// (alphanumeric, ~33 chars). Paths are NOT first-class on Drive — we
/// resolve them by walking the children of the root folder one segment
/// at a time. <see cref="FindFolderByPathAsync"/> handles this.</para>
/// </summary>
public class GoogleDriveCloudStorageService : ICloudStorageIntegrationService
{
    private const string DriveApiBase = "https://www.googleapis.com/drive/v3";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GoogleDriveOptions _options;
    private readonly ILogger<GoogleDriveCloudStorageService> _logger;

    public GoogleDriveCloudStorageService(
        IHttpClientFactory httpClientFactory,
        IOptions<GoogleDriveOptions> options,
        ILogger<GoogleDriveCloudStorageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderCode => "gdrive";

    public async Task<CloudFolder> CreateFolderAsync(
        string accessToken,
        CreateFolderRequest request,
        CancellationToken ct)
    {
        // EnsureExists: try to find an existing folder with this name under
        // the parent before creating a new one. Avoids accumulating
        // same-named folders on retries.
        if (request.EnsureExists)
        {
            var existing = await FindFolderByNameInParentAsync(
                accessToken, request.Name, request.ParentExternalId, ct);
            if (existing is not null)
            {
                _logger.LogInformation("GoogleDrive: CreateFolder ensure-exists hit '{Name}' (id={Id})",
                    request.Name, existing.ExternalId);
                return existing;
            }
        }

        var client = CreateClient(accessToken);
        var body = new
        {
            name = request.Name,
            mimeType = FolderMimeType,
            parents = request.ParentExternalId is null ? null : new[] { request.ParentExternalId },
        };

        var response = await client.PostAsJsonAsync(
            $"{DriveApiBase}/files?fields=id,name,webViewLink,parents", body, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var folder = ParseFolder(json);
        _logger.LogInformation("GoogleDrive: Created folder '{Name}' (id={Id})", folder.Name, folder.ExternalId);
        return folder;
    }

    public async Task<CloudFolder?> GetFolderAsync(
        string accessToken,
        string folderExternalId,
        CancellationToken ct)
    {
        var client = CreateClient(accessToken);
        var response = await client.GetAsync(
            $"{DriveApiBase}/files/{Uri.EscapeDataString(folderExternalId)}?fields=id,name,webViewLink,parents",
            ct);
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
        // Drive's query DSL: only folders (not files), only direct children, only non-trashed.
        var q = $"mimeType='{FolderMimeType}' and '{parentExternalId}' in parents and trashed=false";
        var response = await client.GetAsync(
            $"{DriveApiBase}/files?q={Uri.EscapeDataString(q)}&fields=files(id,name,webViewLink,parents)&pageSize=200",
            ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        if (!json.TryGetProperty("files", out var filesArr) || filesArr.ValueKind != JsonValueKind.Array)
            return [];

        return filesArr.EnumerateArray().Select(ParseFolder).ToList();
    }

    public async Task<CloudFolder?> FindFolderByPathAsync(
        string accessToken,
        string path,
        CancellationToken ct)
    {
        // Walk segments. Drive doesn't have native path lookup; we descend
        // from root looking for each segment in turn.
        var segments = (path ?? string.Empty)
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        string? currentParent = "root";
        CloudFolder? currentFolder = null;
        foreach (var segment in segments)
        {
            currentFolder = await FindFolderByNameInParentAsync(accessToken, segment, currentParent, ct);
            if (currentFolder is null) return null;
            currentParent = currentFolder.ExternalId;
        }
        return currentFolder;
    }

    public async Task<CloudStorageTokenRefreshResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken ct)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("GoogleDrive credentials are not configured.");

        var client = _httpClientFactory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        });
        var response = await client.PostAsync(TokenEndpoint, form, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var access = json.GetProperty("access_token").GetString()!;
        // Google may or may not rotate refresh tokens — if absent, the caller
        // keeps using the same one.
        var newRefresh = json.TryGetProperty("refresh_token", out var rt)
            ? rt.GetString() ?? refreshToken
            : refreshToken;
        var expiresIn = json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        return new CloudStorageTokenRefreshResult(access, newRefresh, expiresAt);
    }

    public async Task<CloudStorageHealthResult> HealthCheckAsync(
        string accessToken,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = CreateClient(accessToken);
            // about?fields=user — minimal call, returns the connected user info.
            var response = await client.GetAsync($"{DriveApiBase}/about?fields=user", ct);
            sw.Stop();
            response.EnsureSuccessStatusCode();
            return new CloudStorageHealthResult(true, ProviderCode, "OK", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "GoogleDrive health check failed");
            return new CloudStorageHealthResult(false, ProviderCode, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private async Task<CloudFolder?> FindFolderByNameInParentAsync(
        string accessToken,
        string name,
        string? parentExternalId,
        CancellationToken ct)
    {
        var parent = parentExternalId ?? "root";
        var client = CreateClient(accessToken);
        // Single-segment lookup. Escape single quotes in the name per Drive's query DSL.
        var safeName = name.Replace("'", "\\'");
        var q = $"name='{safeName}' and mimeType='{FolderMimeType}' and '{parent}' in parents and trashed=false";
        var response = await client.GetAsync(
            $"{DriveApiBase}/files?q={Uri.EscapeDataString(q)}&fields=files(id,name,webViewLink,parents)&pageSize=1",
            ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!json.TryGetProperty("files", out var files) ||
            files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0)
            return null;
        return ParseFolder(files[0]);
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
        var webUrl = json.TryGetProperty("webViewLink", out var w)
            ? w.GetString() ?? $"https://drive.google.com/drive/folders/{id}"
            : $"https://drive.google.com/drive/folders/{id}";
        var parent = json.TryGetProperty("parents", out var parents) &&
                     parents.ValueKind == JsonValueKind.Array &&
                     parents.GetArrayLength() > 0
            ? parents[0].GetString()
            : null;
        // Drive doesn't return a path; the caller composes one from
        // FindFolderByPath if it needs one.
        return new CloudFolder(id, name, Path: $"/{name}", WebUrl: webUrl, ParentExternalId: parent);
    }
}
