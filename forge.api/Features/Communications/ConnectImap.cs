using System.Text.Json;

using FluentValidation;
using MailKit.Security;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models.Communications;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 phase 1h — IMAP-specific connect command. Distinct from the
/// generic <see cref="CreateCommunicationSyncConfigCommand"/> because:
///   1. We test-authenticate against the live IMAP server before persisting,
///      so broken credentials never land as a "connected" row that would
///      then fail every Hangfire tick.
///   2. The password gets encrypted via Data Protection API en route to
///      <c>AccessToken</c>; the client never sends the sealed envelope
///      directly.
///   3. <c>ConfigJson</c> is built server-side from the typed request shape
///      so client mistakes can't poison the JSON.
///
/// On success the row lands with <c>IsConnected=true</c>; the next
/// Hangfire tick (or "Sync now" click) starts polling.
/// </summary>
public record ConnectImapCommand(
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string Password,
    string? Mailbox,
    string? DisplayLabel) : IRequest<CommunicationSyncConfigResponseModel>;

public class ConnectImapValidator : AbstractValidator<ConnectImapCommand>
{
    public ConnectImapValidator()
    {
        RuleFor(x => x.Host).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.Username).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Mailbox).MaximumLength(120);
        RuleFor(x => x.DisplayLabel).MaximumLength(120);
    }
}

public class ConnectImapHandler(
    AppDbContext db,
    IDataProtectionProvider dataProtection,
    IImapClientFactory clientFactory)
    : IRequestHandler<ConnectImapCommand, CommunicationSyncConfigResponseModel>
{
    private const string ProtectorPurpose = "communication-sync.imap";
    private const string ProviderId = "imap";

    public async Task<CommunicationSyncConfigResponseModel> Handle(
        ConnectImapCommand request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("ConnectImap requires an authenticated caller.");

        // Test-authenticate first — refuse to persist a row we know would
        // fail to sync. The user gets immediate feedback ("password rejected"
        // / "host unreachable") rather than a silent broken connection.
        await TestConnectionAsync(request, cancellationToken);

        // Existing row for the same (user, kind, provider, externalAccountId)?
        // Reject — soft-deleting first is the user's job (matches the generic
        // create handler's dedup contract).
        var duplicate = await db.CommunicationSyncConfigs
            .Where(c => c.UserId == userId
                && c.Kind == CommunicationKind.Email
                && c.ProviderId == ProviderId
                && c.ExternalAccountId == request.Username)
            .AnyAsync(cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException(
                $"IMAP connection for {request.Username} already exists for this user.");
        }

        var config = new ImapConnectionConfig
        {
            Host = request.Host,
            Port = request.Port,
            UseSsl = request.UseSsl,
            Username = request.Username,
            Mailbox = string.IsNullOrEmpty(request.Mailbox) ? "INBOX" : request.Mailbox,
        };

        var protector = dataProtection.CreateProtector(ProtectorPurpose);
        var sealedPassword = protector.Protect(request.Password);

        var row = new CommunicationSyncConfig
        {
            UserId = userId,
            Kind = CommunicationKind.Email,
            ProviderId = ProviderId,
            DisplayLabel = request.DisplayLabel,
            ExternalAccountId = request.Username,
            ConfigJson = JsonSerializer.Serialize(config),
            AccessToken = sealedPassword,
            IsConnected = true,
        };

        db.CommunicationSyncConfigs.Add(row);

        db.LogActivityAt(
            "communication-sync-connection-added",
            $"Added email sync connection: imap ({request.Username})"
                + (request.DisplayLabel is null ? string.Empty : $" — {request.DisplayLabel}"),
            ("User", userId));

        await db.SaveChangesAsync(cancellationToken);

        return new CommunicationSyncConfigResponseModel(
            row.Id, row.UserId, row.Kind, row.ProviderId, row.DisplayLabel,
            row.IsConnected, row.ExternalAccountId, row.LastSyncedAt,
            row.LastError, row.LastErrorAt,
            row.CreatedAt, row.UpdatedAt);
    }

    /// <summary>
    /// Open + authenticate + close, with no folder traffic. MailKit's
    /// <see cref="AuthenticationException"/> covers wrong-password cases;
    /// connection-level errors bubble as <see cref="System.Net.Sockets.SocketException"/>
    /// (host unreachable) or generic <see cref="IOException"/>. Translate
    /// each to a recognisable <see cref="InvalidOperationException"/> so
    /// the global ExceptionHandlingMiddleware emits a 409 with the cause.
    /// </summary>
    private async Task TestConnectionAsync(ConnectImapCommand request, CancellationToken ct)
    {
        await using var client = clientFactory.Create();
        try
        {
            await client.ConnectAsync(request.Host, request.Port, request.UseSsl, ct);
            await client.AuthenticateAsync(request.Username, request.Password, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (AuthenticationException)
        {
            throw new InvalidOperationException(
                "IMAP authentication failed — username or password rejected by the server.");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            throw new InvalidOperationException(
                $"IMAP server is unreachable: {ex.Message}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"IMAP connection test failed: {ex.Message}");
        }
    }
}
