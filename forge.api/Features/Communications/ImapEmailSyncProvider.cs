using System.Text.Json;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Enums;
using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Communications;
using Forge.Data.Context;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 — universal IMAP email sync adapter using MailKit. Supports any
/// IMAP server including Gmail, Outlook/Office 365 (with App Password or
/// OAuth-IMAP), Yahoo, Fastmail, custom IMAP servers.
///
/// Authentication: plain username + password (App Password for providers
/// that require it). Password is stored in <see cref="Core.Entities.CommunicationSyncConfig.AccessToken"/>
/// as a sealed envelope via the Data Protection API. OAuth-IMAP (SASL
/// OAUTHBEARER) is a follow-on phase — same adapter, different auth path.
///
/// Polling strategy: UID-based checkpoint stored in
/// <see cref="Core.Entities.CommunicationSyncConfig.LastSyncedExternalId"/>
/// as <c>UIDVALIDITY:LASTUID</c>. On each tick we open the mailbox,
/// verify UIDVALIDITY hasn't changed (if it did, the mailbox was rebuilt
/// and we resync from scratch), then fetch UIDs &gt; LASTUID. UIDVALIDITY+
/// UID is the canonical "this message" identifier per RFC 3501.
///
/// Rate / safety:
/// - Max 100 messages per tick to bound matcher work + IMAP traffic.
/// - On exception we log + rethrow; the outer Hangfire job swallows so a
///   broken connection doesn't starve the rest of the install.
/// - Connection is opened + disposed per tick — IMAP IDLE long-poll is a
///   future optimization once we have a long-running connection layer.
/// </summary>
public class ImapEmailSyncProvider(
    AppDbContext db,
    ICommunicationMatcher matcher,
    IDataProtectionProvider dataProtection,
    IImapClientFactory clientFactory,
    IImapOAuthService oauthService,
    Forge.Core.Interfaces.IClock clock,
    ILogger<ImapEmailSyncProvider> logger) : ICommunicationSyncProvider
{
    private const string ProtectorPurpose = "communication-sync.imap";
    private const int MaxMessagesPerTick = 100;

    /// <summary>Refresh access tokens that expire within this window.
    /// 5 minutes is conservative — most providers honour tokens up to
    /// the exact expiry, but token issuance has clock drift.</summary>
    private static readonly TimeSpan AccessTokenRefreshWindow = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public string ProviderId => "imap";
    public CommunicationKind Kind => CommunicationKind.Email;

    public Task<string?> StartAuthAsync(int userId, CancellationToken ct)
        => Task.FromResult<string?>(null); // No OAuth round-trip; user enters creds at connect time.

    public Task<bool> CompleteAuthAsync(int userId, string code, CancellationToken ct)
        => Task.FromResult(true);

    public async Task<int> SyncRecentAsync(int connectionId, CancellationToken ct)
    {
        var connection = await db.CommunicationSyncConfigs
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (connection is null)
        {
            logger.LogWarning("ImapEmailSyncProvider: connection {Id} not found; skipping", connectionId);
            return 0;
        }

        if (string.IsNullOrEmpty(connection.ConfigJson) || string.IsNullOrEmpty(connection.AccessToken))
        {
            logger.LogWarning(
                "ImapEmailSyncProvider: connection {Id} missing ConfigJson or AccessToken; skipping", connectionId);
            return 0;
        }

        ImapConnectionConfig config;
        try
        {
            config = JsonSerializer.Deserialize<ImapConnectionConfig>(connection.ConfigJson)
                ?? throw new InvalidOperationException("ConfigJson deserialized to null");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ImapEmailSyncProvider: invalid ConfigJson on connection {Id}", connectionId);
            return 0;
        }

        var (lastUidValidity, lastUid) = ParseCheckpoint(connection.LastSyncedExternalId);

        await using var client = clientFactory.Create();
        try
        {
            await client.ConnectAsync(config.Host, config.Port, config.UseSsl, ct);

            // Phase 1k.2 — branch on AuthMethod. Password path unchanged
            // from phase 1g; OAuth path refreshes the access_token if
            // expiring within AccessTokenRefreshWindow, then SASL
            // OAUTHBEARER authenticates with the bearer token.
            if (config.AuthMethod == "oauth")
            {
                var bearer = await EnsureFreshAccessTokenAsync(connection, config, ct);
                await client.AuthenticateOAuthAsync(config.Username, bearer, ct);
            }
            else
            {
                string password;
                try
                {
                    password = _protector.Unprotect(connection.AccessToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "ImapEmailSyncProvider: failed to unprotect AccessToken on connection {Id} — re-connect may be needed",
                        connectionId);
                    return 0;
                }
                await client.AuthenticateAsync(config.Username, password, ct);
            }

            var folder = await client.OpenFolderAsync(config.Mailbox, ct);
            var uidValidity = folder.UidValidity;

            // UIDVALIDITY changed = mailbox was rebuilt; reset checkpoint
            // and skip this tick (next tick fetches from "new" baseline).
            if (lastUidValidity is not null && uidValidity != lastUidValidity)
            {
                logger.LogInformation(
                    "ImapEmailSyncProvider: UIDVALIDITY changed on connection {Id} (was {Was}, now {Now}) — checkpoint reset",
                    connectionId, lastUidValidity, uidValidity);
                connection.LastSyncedExternalId = $"{uidValidity}:0";
                await db.SaveChangesAsync(ct);
                return 0;
            }

            // First sync ever: don't backfill the entire mailbox; just
            // checkpoint the current high-water mark and ingest from
            // here forward. Otherwise a 50K-message mailbox would log
            // 50K activity rows the first time someone connects.
            if (lastUidValidity is null)
            {
                var highWater = await folder.GetHighestUidAsync(ct);
                connection.LastSyncedExternalId = $"{uidValidity}:{highWater}";
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "ImapEmailSyncProvider: connection {Id} initial checkpoint = {Uid} (mailbox not backfilled)",
                    connectionId, highWater);
                return 0;
            }

            var uids = await folder.SearchUidsAsync(SearchQuery.Uids(new UniqueIdRange(
                new UniqueId(uidValidity, lastUid + 1), UniqueId.MaxValue)), ct);

            if (uids.Count == 0)
            {
                connection.LastSyncedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return 0;
            }

            var batch = uids.Take(MaxMessagesPerTick).ToList();
            var matched = 0;
            uint highestUid = lastUid;

            foreach (var uid in batch)
            {
                ct.ThrowIfCancellationRequested();
                var msg = await folder.FetchMessageAsync(uid, ct);
                if (msg is null) continue;

                var comm = TranslateMimeMessage(msg, uid, config.Username);
                var result = await matcher.MatchAndLogAsync(comm, ct);
                if (result.Matched) matched++;
                if (uid.Id > highestUid) highestUid = uid.Id;
            }

            connection.LastSyncedExternalId = $"{uidValidity}:{highestUid}";
            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "ImapEmailSyncProvider: connection {Id} processed {Count} messages, matched {Matched}",
                connectionId, batch.Count, matched);

            return matched;
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, CancellationToken.None);
            }
        }
    }

    public Task IngestWebhookEventAsync(string rawPayload, CancellationToken ct)
        => Task.CompletedTask; // IMAP is polling-only.

    /// <summary>
    /// Phase 1k.2 — refresh the OAuth access token if it's within the
    /// refresh window, then return the (possibly fresh) access token in
    /// plaintext for the SASL OAUTHBEARER auth step. Refresh-token
    /// rotation: when the provider returns a new refresh_token, persist
    /// it (Microsoft does this; Google generally doesn't but the contract
    /// allows for either).
    /// </summary>
    private async Task<string> EnsureFreshAccessTokenAsync(
        Forge.Core.Entities.CommunicationSyncConfig connection,
        ImapConnectionConfig config,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(connection.AccessToken))
        {
            throw new InvalidOperationException("OAuth connection has no access token — re-authorize.");
        }
        if (string.IsNullOrEmpty(connection.RefreshToken))
        {
            throw new InvalidOperationException("OAuth connection has no refresh token — re-authorize.");
        }
        if (string.IsNullOrEmpty(config.OAuthProvider))
        {
            throw new InvalidOperationException("OAuth connection ConfigJson is missing the OAuthProvider field.");
        }

        var needsRefresh =
            connection.AccessTokenExpiresAt is null
            || connection.AccessTokenExpiresAt.Value - clock.UtcNow <= AccessTokenRefreshWindow;

        if (!needsRefresh)
        {
            return _protector.Unprotect(connection.AccessToken);
        }

        var refreshToken = _protector.Unprotect(connection.RefreshToken);
        var refreshed = await oauthService.RefreshAccessTokenAsync(config.OAuthProvider, refreshToken, ct);

        connection.AccessToken = _protector.Protect(refreshed.AccessToken);
        if (!string.IsNullOrEmpty(refreshed.NewRefreshToken))
        {
            connection.RefreshToken = _protector.Protect(refreshed.NewRefreshToken);
        }
        connection.AccessTokenExpiresAt = refreshed.AccessTokenExpiresAt;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ImapEmailSyncProvider: refreshed access_token for connection {Id} ({Provider})",
            connection.Id, config.OAuthProvider);

        return refreshed.AccessToken;
    }

    /// <summary>
    /// Parse <see cref="Core.Entities.CommunicationSyncConfig.LastSyncedExternalId"/>
    /// as "UIDVALIDITY:LASTUID". Returns (null, 0) for first-time syncs.
    /// </summary>
    internal static (uint? UidValidity, uint LastUid) ParseCheckpoint(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return (null, 0);
        var parts = raw.Split(':');
        if (parts.Length != 2) return (null, 0);
        if (!uint.TryParse(parts[0], out var uidValidity)) return (null, 0);
        if (!uint.TryParse(parts[1], out var lastUid)) return (uidValidity, 0);
        return (uidValidity, lastUid);
    }

    /// <summary>
    /// Translate a fetched <see cref="MimeMessage"/> + UID to the matcher's
    /// <see cref="InboundCommunication"/> envelope. The user's own mailbox
    /// address (<paramref name="ownerAddress"/>) is used to determine
    /// inbound vs outbound — if the user is in From, this was sent by them.
    /// </summary>
    internal static InboundCommunication TranslateMimeMessage(MimeMessage msg, UniqueId uid, string ownerAddress)
    {
        var fromAddr = ExtractFirstAddress(msg.From) ?? string.Empty;
        var toAddrs = msg.To.Mailboxes.Select(m => m.Address).ToList();
        var ccAddrs = msg.Cc.Mailboxes.Select(m => m.Address).ToList();

        var direction = string.Equals(fromAddr, ownerAddress, StringComparison.OrdinalIgnoreCase)
            ? CommunicationDirection.Outbound
            : CommunicationDirection.Inbound;

        return new InboundCommunication(
            ProviderId: "imap",
            Kind: CommunicationKind.Email,
            Direction: direction,
            ExternalId: $"imap-{uid.Validity}-{uid.Id}",
            From: fromAddr,
            To: [..toAddrs, ..ccAddrs],
            OccurredAt: msg.Date == default ? DateTimeOffset.UtcNow : msg.Date,
            Subject: msg.Subject ?? "(no subject)",
            Body: BuildBodyPreview(msg),
            DurationMinutes: null,
            RecordingUrl: null);
    }

    private static string? ExtractFirstAddress(InternetAddressList list)
        => list.Mailboxes.FirstOrDefault()?.Address;

    private static string? BuildBodyPreview(MimeMessage msg)
    {
        var text = msg.TextBody ?? msg.HtmlBody;
        if (string.IsNullOrEmpty(text)) return null;
        // Trim to a reasonable preview length for the activity log.
        return text.Length <= 2000 ? text : text[..2000];
    }
}

/// <summary>
/// Wave 8 — factory that lets tests substitute the IMAP client. Production
/// returns a real <see cref="ImapClient"/>; unit tests inject a fake.
/// </summary>
public interface IImapClientFactory
{
    IImapClientWrapper Create();
}

/// <summary>
/// Thin abstraction over MailKit's <see cref="ImapClient"/> + the few
/// folder operations we use. Lets the adapter be unit-tested without a
/// live IMAP server.
/// </summary>
public interface IImapClientWrapper : IAsyncDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(string host, int port, bool useSsl, CancellationToken ct);
    Task AuthenticateAsync(string username, string password, CancellationToken ct);

    /// <summary>
    /// Phase 1k.2 — SASL OAUTHBEARER authentication for OAuth-IMAP. The
    /// access token is the plaintext bearer (already-refreshed by the
    /// caller); MailKit handles the OAUTHBEARER mechanism handshake.
    /// </summary>
    Task AuthenticateOAuthAsync(string username, string accessToken, CancellationToken ct);

    Task<IImapFolderWrapper> OpenFolderAsync(string mailbox, CancellationToken ct);
    Task DisconnectAsync(bool quit, CancellationToken ct);
}

public interface IImapFolderWrapper
{
    uint UidValidity { get; }
    Task<uint> GetHighestUidAsync(CancellationToken ct);
    Task<IList<UniqueId>> SearchUidsAsync(SearchQuery query, CancellationToken ct);
    Task<MimeMessage?> FetchMessageAsync(UniqueId uid, CancellationToken ct);
}

/// <summary>Production factory backed by real MailKit.</summary>
public class ImapClientFactory : IImapClientFactory
{
    public IImapClientWrapper Create() => new RealImapClientWrapper();

    private sealed class RealImapClientWrapper : IImapClientWrapper
    {
        private readonly ImapClient _client = new();

        public bool IsConnected => _client.IsConnected;

        public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken ct)
            => _client.ConnectAsync(host, port, useSsl, ct);

        public Task AuthenticateAsync(string username, string password, CancellationToken ct)
            => _client.AuthenticateAsync(username, password, ct);

        public Task AuthenticateOAuthAsync(string username, string accessToken, CancellationToken ct)
        {
            // MailKit's SASL OAUTHBEARER mechanism consumes a SaslMechanism.
            // SaslMechanismOAuthBearer wraps user+token and emits the
            // RFC 7628 client-initial-response when the IMAP server
            // advertises AUTH=OAUTHBEARER.
            var sasl = new MailKit.Security.SaslMechanismOAuthBearer(username, accessToken);
            return _client.AuthenticateAsync(sasl, ct);
        }

        public async Task<IImapFolderWrapper> OpenFolderAsync(string mailbox, CancellationToken ct)
        {
            IMailFolder? folder;
            if (mailbox.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            {
                folder = _client.Inbox;
            }
            else
            {
                folder = await _client.GetFolderAsync(mailbox, ct);
            }
            if (folder is null)
            {
                throw new InvalidOperationException($"IMAP folder '{mailbox}' not found");
            }
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            return new RealImapFolderWrapper(folder);
        }

        public Task DisconnectAsync(bool quit, CancellationToken ct)
            => _client.DisconnectAsync(quit, ct);

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RealImapFolderWrapper(IMailFolder folder) : IImapFolderWrapper
    {
        public uint UidValidity => folder.UidValidity;

        public async Task<uint> GetHighestUidAsync(CancellationToken ct)
        {
            // Refresh folder STATUS so UidNext reflects the latest state;
            // UIDNEXT - 1 is the last assigned UID. Empty folder: 0.
            await folder.StatusAsync(StatusItems.UidNext, ct);
            return folder.UidNext is { } next && next.Id > 0 ? next.Id - 1 : 0;
        }

        public async Task<IList<UniqueId>> SearchUidsAsync(SearchQuery query, CancellationToken ct)
            => await folder.SearchAsync(query, ct);

        public async Task<MimeMessage?> FetchMessageAsync(UniqueId uid, CancellationToken ct)
        {
            try { return await folder.GetMessageAsync(uid, ct); }
            catch (MessageNotFoundException) { return null; }
        }
    }
}
