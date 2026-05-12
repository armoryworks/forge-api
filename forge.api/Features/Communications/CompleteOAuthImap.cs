using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models.Communications;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 phase 1k.2 — complete the OAuth-IMAP round-trip. The SPA's
/// callback page receives <c>code</c> + <c>state</c> from the provider
/// redirect, posts them here. We:
///   1. Look up the state token. Reject if missing, expired, or owned by
///      a different user (CSRF guard).
///   2. Consume the state token (one-shot).
///   3. Exchange the code for tokens via the provider's token endpoint.
///   4. Encrypt access + refresh tokens via Data Protection API.
///   5. Persist a new <see cref="CommunicationSyncConfig"/> row with
///      AuthMethod="oauth", IsConnected=true. Refuse if a row already
///      exists for the same email — user must disconnect first.
/// </summary>
public record CompleteOAuthImapCommand(
    string ProviderKey,
    string Code,
    string State) : IRequest<CommunicationSyncConfigResponseModel>;

public class CompleteOAuthImapValidator : AbstractValidator<CompleteOAuthImapCommand>
{
    public CompleteOAuthImapValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.State).NotEmpty().Length(64); // hex 32-byte → 64 chars
        RuleFor(x => x.ProviderKey).NotEmpty().MaximumLength(32);
    }
}

public class CompleteOAuthImapHandler(
    AppDbContext db,
    IImapOAuthService oauth,
    IDataProtectionProvider dataProtection,
    IClock clock)
    : IRequestHandler<CompleteOAuthImapCommand, CommunicationSyncConfigResponseModel>
{
    private const string ProtectorPurpose = "communication-sync.imap";

    public async Task<CommunicationSyncConfigResponseModel> Handle(
        CompleteOAuthImapCommand request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("CompleteOAuthImap requires an authenticated caller.");

        var providerKey = request.ProviderKey.ToLowerInvariant();
        var provider = ImapOAuthProvider.FromKey(providerKey)
            ?? throw new InvalidOperationException($"Unknown OAuth provider: {request.ProviderKey}");

        // 1 — validate state token. Owned-by-this-user enforcement is the
        // CSRF guard; expiry guards against stale tokens being replayed
        // weeks later if leaked.
        var state = await db.OAuthStateTokens.FirstOrDefaultAsync(
            t => t.Token == request.State, cancellationToken);
        if (state is null || state.UserId != userId || state.ProviderKey != providerKey)
        {
            throw new InvalidOperationException(
                "Invalid OAuth state. The authorization request may have expired — please retry.");
        }
        if (state.ExpiresAt < clock.UtcNow)
        {
            db.OAuthStateTokens.Remove(state);
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(
                "OAuth state expired (10-minute window). Please retry the connect flow.");
        }

        // 2 — one-shot: consume the state token before any external I/O so
        // a half-completed flow (network dies during code exchange) can't
        // be replayed.
        db.OAuthStateTokens.Remove(state);
        await db.SaveChangesAsync(cancellationToken);

        // 3 — exchange.
        var tokens = await oauth.ExchangeCodeForTokensAsync(providerKey, request.Code, cancellationToken);

        // 4 — refuse if an OAuth-IMAP connection already exists for this
        // (user, provider, email). User must disconnect first to re-auth.
        var duplicate = await db.CommunicationSyncConfigs
            .Where(c => c.UserId == userId
                && c.Kind == CommunicationKind.Email
                && c.ProviderId == "imap"
                && c.ExternalAccountId == tokens.EmailAddress)
            .AnyAsync(cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException(
                $"A connection for {tokens.EmailAddress} already exists. Disconnect it first to re-authorize.");
        }

        // 5 — persist with AuthMethod=oauth + sealed tokens.
        var protector = dataProtection.CreateProtector(ProtectorPurpose);
        var config = new ImapConnectionConfig
        {
            Host = provider.ImapHost,
            Port = provider.ImapPort,
            UseSsl = true,
            Username = tokens.EmailAddress,
            Mailbox = "INBOX",
            AuthMethod = "oauth",
            OAuthProvider = providerKey,
        };

        var row = new CommunicationSyncConfig
        {
            UserId = userId,
            Kind = CommunicationKind.Email,
            ProviderId = "imap",
            DisplayLabel = provider.DisplayName,
            ExternalAccountId = tokens.EmailAddress,
            ConfigJson = JsonSerializer.Serialize(config),
            AccessToken = protector.Protect(tokens.AccessToken),
            RefreshToken = tokens.RefreshToken is null ? null : protector.Protect(tokens.RefreshToken),
            AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
            IsConnected = true,
        };

        db.CommunicationSyncConfigs.Add(row);

        db.LogActivityAt(
            "communication-sync-connection-added",
            $"Added email sync connection: {provider.DisplayName} ({tokens.EmailAddress}) via OAuth",
            ("User", userId));

        await db.SaveChangesAsync(cancellationToken);

        return new CommunicationSyncConfigResponseModel(
            row.Id, row.UserId, row.Kind, row.ProviderId, row.DisplayLabel,
            row.IsConnected, row.ExternalAccountId, row.LastSyncedAt,
            row.LastError, row.LastErrorAt,
            row.CreatedAt, row.UpdatedAt);
    }
}
