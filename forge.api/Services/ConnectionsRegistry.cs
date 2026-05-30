using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// EF-based implementation of <see cref="IConnectionsRegistry"/>. One private
/// fetch method per source; the public <see cref="ListAsync"/> just stitches
/// them. Each source query is independent — adding a new integration kind is
/// a new private method + one line in <see cref="ListAsync"/>.
///
/// <para><b>Performance.</b> Each source is at most one EF query (Postgres);
/// they're sequential to keep the DbContext single-threaded (EF Core
/// requirement). The total wire weight is small — even a busy install rarely
/// holds more than a few hundred connection rows across all sources.</para>
/// </summary>
public class ConnectionsRegistry(
    AppDbContext db,
    ISystemSettingRepository systemSettings) : IConnectionsRegistry
{
    public async Task<List<IntegrationRecordResponseModel>> ListAsync(CancellationToken ct)
    {
        var rows = new List<IntegrationRecordResponseModel>();
        rows.AddRange(await FetchBiApiKeysAsync(ct));
        rows.AddRange(await FetchSystemApiKeysAsync(ct));
        rows.AddRange(await FetchEdiTradingPartnersAsync(ct));
        rows.AddRange(await FetchQuickBooksAsync(ct));
        rows.AddRange(await FetchCommunicationSyncAsync(ct));
        rows.AddRange(await FetchCloudStorageLinksAsync(ct));
        // Sort by most-recently-touched first; rows without a usage or
        // creation timestamp drop to the bottom.
        return rows
            .OrderByDescending(r => r.LastUsedAt ?? r.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<List<IntegrationRecordResponseModel>> FetchBiApiKeysAsync(CancellationToken ct)
    {
        return await db.BiApiKeys.AsNoTracking()
            .Select(k => new IntegrationRecordResponseModel
            {
                Kind = IntegrationKind.BiApiKey,
                SourceId = k.Id.ToString(),
                Name = k.Name,
                OwnerEmail = null, // BI keys are unbound (synthetic principal)
                Status = k.IsActive
                    ? (k.ExpiresAt != null && k.ExpiresAt < DateTimeOffset.UtcNow ? "Expired" : "Active")
                    : "Revoked",
                LastUsedAt = k.LastUsedAt,
                CreatedAt = k.CreatedAt,
                ManageRoute = "/admin/bi-api-keys",
            })
            .ToListAsync(ct);
    }

    private async Task<List<IntegrationRecordResponseModel>> FetchSystemApiKeysAsync(CancellationToken ct)
    {
        return await (
            from k in db.SystemApiKeys.AsNoTracking()
            join u in db.Users.AsNoTracking() on k.UserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new IntegrationRecordResponseModel
            {
                Kind = IntegrationKind.SystemApiKey,
                SourceId = k.Id.ToString(),
                Name = k.Name,
                OwnerEmail = u != null ? u.Email : null,
                Status = k.IsActive
                    ? (k.ExpiresAt != null && k.ExpiresAt < DateTimeOffset.UtcNow ? "Expired" : "Active")
                    : "Revoked",
                LastUsedAt = k.LastUsedAt,
                CreatedAt = k.CreatedAt,
                ManageRoute = "/admin/system-api-keys",
            }).ToListAsync(ct);
    }

    private async Task<List<IntegrationRecordResponseModel>> FetchEdiTradingPartnersAsync(CancellationToken ct)
    {
        return await db.EdiTradingPartners.AsNoTracking()
            .Select(p => new IntegrationRecordResponseModel
            {
                Kind = IntegrationKind.EdiTradingPartner,
                SourceId = p.Id.ToString(),
                Name = p.Name,
                OwnerEmail = null, // partner is install-level, not user-bound
                Status = p.IsActive ? "Active" : "Inactive",
                LastUsedAt = null, // EdiTradingPartner doesn't track last-used; transactions do
                CreatedAt = p.CreatedAt,
                ManageRoute = "/admin/edi",
            })
            .ToListAsync(ct);
    }

    private async Task<List<IntegrationRecordResponseModel>> FetchQuickBooksAsync(CancellationToken ct)
    {
        // QuickBooks OAuth is persisted as a SystemSetting blob keyed by
        // `qb_oauth_token` (see QuickBooksTokenService). Singleton — at most
        // one connection per install. We only surface presence + the
        // last-modified timestamp from the setting row; the payload itself
        // is the encrypted token envelope.
        var token = await systemSettings.FindByKeyAsync("qb_oauth_token", ct);
        if (token is null)
            return new List<IntegrationRecordResponseModel>();
        return new List<IntegrationRecordResponseModel>
        {
            new()
            {
                Kind = IntegrationKind.QuickBooksOAuth,
                SourceId = "qb_oauth_token",
                Name = "QuickBooks Online",
                OwnerEmail = null,
                Status = "Connected",
                LastUsedAt = null, // SystemSetting carries no timestamps;
                CreatedAt = null,  // exposed as connected but undated
                ManageRoute = "/admin/integrations",
            },
        };
    }

    private async Task<List<IntegrationRecordResponseModel>> FetchCommunicationSyncAsync(CancellationToken ct)
    {
        return await (
            from c in db.CommunicationSyncConfigs.AsNoTracking()
            join u in db.Users.AsNoTracking() on c.UserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new IntegrationRecordResponseModel
            {
                Kind = IntegrationKind.CommunicationSync,
                SourceId = c.Id.ToString(),
                Name = (c.DisplayLabel ?? c.ProviderId) + " (" + c.Kind.ToString() + ")",
                OwnerEmail = u != null ? u.Email : null,
                Status = c.IsConnected ? "Connected" : "Disconnected",
                LastUsedAt = c.LastSyncedAt,
                CreatedAt = c.CreatedAt,
                ManageRoute = "/account/communications",
            }).ToListAsync(ct);
    }

    private async Task<List<IntegrationRecordResponseModel>> FetchCloudStorageLinksAsync(CancellationToken ct)
    {
        return await (
            from l in db.UserCloudStorageLinks.AsNoTracking()
            join p in db.CloudStorageProviders.AsNoTracking() on l.ProviderId equals p.Id into pj
            from p in pj.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on l.UserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new IntegrationRecordResponseModel
            {
                Kind = IntegrationKind.CloudStorageLink,
                SourceId = l.Id.ToString(),
                Name = p != null ? p.ProviderCode : "(unknown provider)",
                OwnerEmail = u != null ? u.Email : null,
                Status = "Linked",
                LastUsedAt = null, // links don't currently track last-use directly
                CreatedAt = l.CreatedAt,
                ManageRoute = "/account/cloud-storage",
            }).ToListAsync(ct);
    }
}
