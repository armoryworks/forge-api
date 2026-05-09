using System.Security.Cryptography;

using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Entities;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models.Communications;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.Communications;

/// <summary>
/// Wave 8 phase 1k.2 — initiate the OAuth-IMAP authorization round-trip.
/// Generates a 256-bit random state token, persists it bound to the
/// caller's user id, returns the authorize URL the SPA opens in a popup
/// or new tab.
///
/// State validity: 10 minutes from creation. Long enough that a real user
/// can complete a Google/MS consent screen (some flows include account
/// switching, MFA, app re-consent), short enough that an unused token
/// from yesterday can't lurk and be replayed.
/// </summary>
public record BeginOAuthImapCommand(string ProviderKey) : IRequest<BeginOAuthImapResult>;

public sealed record BeginOAuthImapResult(string AuthorizeUrl, string State);

public class BeginOAuthImapHandler(
    AppDbContext db,
    IImapOAuthService oauth,
    IClock clock) : IRequestHandler<BeginOAuthImapCommand, BeginOAuthImapResult>
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public async Task<BeginOAuthImapResult> Handle(BeginOAuthImapCommand request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("BeginOAuthImap requires an authenticated caller.");

        if (!await oauth.IsProviderConfiguredAsync(request.ProviderKey, cancellationToken))
        {
            throw new InvalidOperationException(
                $"OAuth provider '{request.ProviderKey}' is not configured on this install. "
                + "An admin must set the credentials under Admin → Settings → Email — OAuth.");
        }

        // 256-bit random, hex-encoded → 64 hex chars. URL-safe without
        // base64 padding-quirks.
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        db.OAuthStateTokens.Add(new OAuthStateToken
        {
            Token = state,
            UserId = userId,
            ProviderKey = request.ProviderKey.ToLowerInvariant(),
            ExpiresAt = clock.UtcNow.Add(StateLifetime),
        });
        await db.SaveChangesAsync(cancellationToken);

        var url = await oauth.BuildAuthorizeUrlAsync(request.ProviderKey, state, cancellationToken);
        return new BeginOAuthImapResult(url, state);
    }
}
