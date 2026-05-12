using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models.Communications;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 — create a new mailbox / phone connection for the calling user.
/// Starts in <c>IsConnected=false</c>; the OAuth (or webhook-verification)
/// round-trip flips it true. Uniqueness on (UserId, Kind, ProviderId,
/// ExternalAccountId) is enforced here at the application tier — the EF
/// index covers the lookup but isn't unique because re-connecting the
/// same provider after a soft-delete is allowed (current row's
/// IsConnected just stays false until handshake completes).
/// </summary>
public record CreateCommunicationSyncConfigCommand(
    CommunicationKind Kind,
    string ProviderId,
    string? DisplayLabel,
    string? ExternalAccountId,
    string? ConfigJson) : IRequest<CommunicationSyncConfigResponseModel>;

public class CreateCommunicationSyncConfigValidator : AbstractValidator<CreateCommunicationSyncConfigCommand>
{
    public CreateCommunicationSyncConfigValidator()
    {
        RuleFor(x => x.ProviderId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DisplayLabel).MaximumLength(120);
        RuleFor(x => x.ExternalAccountId).MaximumLength(200);
    }
}

public class CreateCommunicationSyncConfigHandler(AppDbContext db)
    : IRequestHandler<CreateCommunicationSyncConfigCommand, CommunicationSyncConfigResponseModel>
{
    public async Task<CommunicationSyncConfigResponseModel> Handle(
        CreateCommunicationSyncConfigCommand request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("CreateCommunicationSyncConfig requires an authenticated caller.");

        // App-tier uniqueness — one active connection per (user, kind,
        // provider, externalAccountId). Re-connect after delete is fine
        // (soft-deleted rows are filtered by the global query filter).
        var duplicate = await db.CommunicationSyncConfigs
            .Where(c => c.UserId == userId
                && c.Kind == request.Kind
                && c.ProviderId == request.ProviderId
                && c.ExternalAccountId == request.ExternalAccountId)
            .AnyAsync(cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException(
                $"Connection for {request.ProviderId} ({request.Kind}) already exists for this user.");
        }

        var config = new CommunicationSyncConfig
        {
            UserId = userId,
            Kind = request.Kind,
            ProviderId = request.ProviderId,
            DisplayLabel = request.DisplayLabel,
            ExternalAccountId = request.ExternalAccountId,
            ConfigJson = request.ConfigJson,
            IsConnected = false,
        };

        db.CommunicationSyncConfigs.Add(config);

        // Activity log on the user (the only natural anchor — sync configs
        // are personal). Verb mirrors other "added an external link" verbs.
        db.LogActivityAt(
            "communication-sync-connection-added",
            $"Added {request.Kind.ToString().ToLowerInvariant()} sync connection: {request.ProviderId}"
                + (request.DisplayLabel is null ? string.Empty : $" ({request.DisplayLabel})"),
            ("User", userId));

        await db.SaveChangesAsync(cancellationToken);

        return new CommunicationSyncConfigResponseModel(
            config.Id, config.UserId, config.Kind, config.ProviderId, config.DisplayLabel,
            config.IsConnected, config.ExternalAccountId, config.LastSyncedAt,
            config.LastError, config.LastErrorAt,
            config.CreatedAt, config.UpdatedAt);
    }
}
