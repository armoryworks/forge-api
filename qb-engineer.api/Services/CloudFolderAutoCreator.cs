using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using QBEngineer.Api.Capabilities.Discovery.Bundles;
using QBEngineer.Api.Features.Presets.Apply.Layers;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Services;

/// <summary>
/// Default <see cref="ICloudFolderAutoCreator"/> — sync-best-effort
/// folder creation per the dual-path design (D2). Reads the active
/// FolderMapBundle from <c>system_settings</c>, resolves the path
/// template, calls the active cloud provider, persists an
/// <see cref="EntityCloudLink"/> row.
///
/// <para>This is the "happy path." Failures are logged but don't
/// propagate — admin can manually attach a folder later. Phase 3a
/// adds outbox-retry on top of this so transient provider outages
/// don't permanently miss the folder.</para>
/// </summary>
public class CloudFolderAutoCreator : ICloudFolderAutoCreator
{
    private readonly AppDbContext _db;
    private readonly IFolderPathResolver _pathResolver;
    private readonly ICloudStorageResolver _providerResolver;
    private readonly ICloudStorageTokenManager _tokenManager;
    private readonly ILogger<CloudFolderAutoCreator> _logger;

    public CloudFolderAutoCreator(
        AppDbContext db,
        IFolderPathResolver pathResolver,
        ICloudStorageResolver providerResolver,
        ICloudStorageTokenManager tokenManager,
        ILogger<CloudFolderAutoCreator> logger)
    {
        _db = db;
        _pathResolver = pathResolver;
        _providerResolver = providerResolver;
        _tokenManager = tokenManager;
        _logger = logger;
    }

    public async Task<EntityCloudLink?> AutoCreateAsync(
        string entityType,
        int entityId,
        IReadOnlyDictionary<string, string> tokenContext,
        CancellationToken ct)
    {
        try
        {
            // 1. Look up the suggestion for this entity type.
            var suggestion = await FindSuggestionForEntityTypeAsync(entityType, ct);
            if (suggestion is null)
            {
                _logger.LogDebug("CloudFolderAutoCreator: no folder map suggestion for entity type '{Type}'; skipping", entityType);
                return null;
            }
            if (!suggestion.AutoCreateOnEntityCreate)
            {
                _logger.LogDebug("CloudFolderAutoCreator: suggestion for '{Type}' has AutoCreateOnEntityCreate=false; skipping", entityType);
                return null;
            }

            // 2. Resolve the path template.
            var path = _pathResolver.Resolve(suggestion.PathTemplate, tokenContext);

            // 3. Find an active CloudStorageProvider row + pick the matching service.
            //    Tracked load (NOT AsNoTracking) — the token manager may mutate
            //    the row to persist a refreshed access token.
            var providerRow = await _db.CloudStorageProviders
                .FirstOrDefaultAsync(p => p.IsActive, ct);
            if (providerRow is null)
            {
                _logger.LogDebug("CloudFolderAutoCreator: no active CloudStorageProvider row; skipping");
                return null;
            }

            var service = _providerResolver.ResolveByCode(providerRow.ProviderCode);
            if (service is null)
            {
                _logger.LogWarning(
                    "CloudFolderAutoCreator: provider row references unregistered code '{Code}'; skipping",
                    providerRow.ProviderCode);
                return null;
            }

            // 4. Resolve a valid access token via the token manager (handles
            //    decrypt + proactive refresh-if-near-expiry + persist rotation).
            var token = await _tokenManager.GetValidAccessTokenAsync(providerRow, ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning(
                    "CloudFolderAutoCreator: provider '{Code}' has no usable access token; skipping",
                    providerRow.ProviderCode);
                return null;
            }

            // 5. Sync best-effort folder creation (EnsureExists keeps the call
            //    idempotent on retry). If the call 401s — meaning the token
            //    expired between the manager check and the call, or was revoked
            //    server-side — log and surface; subsequent runs of the auto-
            //    creator will pick up the rotated token from the manager.
            var folderName = ExtractLastSegment(path);
            var parentExternalId = providerRow.RootFolderId;
            CloudFolder folder;
            try
            {
                folder = await service.CreateFolderAsync(
                    token,
                    new CreateFolderRequest(folderName, parentExternalId, EnsureExists: true),
                    ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(ex,
                    "CloudFolderAutoCreator: provider {Code} returned 401 on CreateFolder — token may be revoked; skipping",
                    providerRow.ProviderCode);
                providerRow.LastError = "Provider returned 401 on CreateFolder — admin may need to reconnect.";
                await _db.SaveChangesAsync(ct);
                return null;
            }

            // 6. Optionally create subfolders. Failures here are non-fatal.
            foreach (var sub in suggestion.SubfolderNames)
            {
                try
                {
                    await service.CreateFolderAsync(
                        token,
                        new CreateFolderRequest(sub, folder.ExternalId, EnsureExists: true),
                        ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "CloudFolderAutoCreator: failed to create subfolder '{Sub}' under '{Parent}' — continuing",
                        sub, folder.ExternalId);
                }
            }

            // 6. Persist EntityCloudLink — one row per (entity, provider).
            var link = new EntityCloudLink
            {
                EntityType = entityType,
                EntityId = entityId,
                ProviderId = providerRow.Id,
                FolderExternalId = folder.ExternalId,
                FolderPath = folder.Path,
                FolderUrl = folder.WebUrl,
                CreatedByUserId = ResolveCreatedByUserGuid(),
                CreatedVia = "auto_create",
            };
            _db.EntityCloudLinks.Add(link);
            await _db.SaveChangesAsync(ct);
            return link;
        }
        catch (Exception ex)
        {
            // Sync best-effort failure — log and move on per D2 dual-path.
            // Phase 3a wires outbox retry here.
            _logger.LogWarning(ex,
                "CloudFolderAutoCreator: sync folder create failed for {Type}/{Id} — admin can attach manually",
                entityType, entityId);
            return null;
        }
    }

    private async Task<FolderMapSuggestion?> FindSuggestionForEntityTypeAsync(
        string entityType, CancellationToken ct)
    {
        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == FolderMapBundleApplier.FolderMapSettingKey, ct);
        if (setting is null || string.IsNullOrEmpty(setting.Value)) return null;

        var suggestions = JsonSerializer.Deserialize<List<FolderMapSuggestion>>(setting.Value);
        return suggestions?.FirstOrDefault(s =>
            string.Equals(s.EntityType, entityType, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractLastSegment(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? string.Empty : segments[^1];
    }

    private Guid? ResolveCreatedByUserGuid()
    {
        // db.CurrentUserId is int? today; EntityCloudLink.CreatedByUserId
        // is Guid? for ASP.NET Identity parity. The auto-create flow runs
        // in a handler context after the entity create; the int → Guid
        // mapping is a Phase 3a concern. For now we leave it null and
        // let CreatedVia="auto_create" be the source-of-truth.
        return null;
    }
}
