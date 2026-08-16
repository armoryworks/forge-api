using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models.Communications;
using Forge.Data.Context;

namespace Forge.Api.Features.Communications;

/// <summary>
/// One communication with everything the review screen needs to make a decision:
/// the message, its hashed artifacts, what it was filed against, and — when it
/// exists — the chain of prior agreements the party has on file.
///
/// <para>The chain is the point. A customer's purchase order authorizes nothing
/// on its own; it authorizes because a master agreement signed years ago says it
/// does. A reviewer who cannot see that chain is being asked to approve on
/// trust.</para>
/// </summary>
public record GetCommunicationDetailQuery(int Id) : IRequest<CommunicationDetailResponseModel>;

public class GetCommunicationDetailHandler(AppDbContext db)
    : IRequestHandler<GetCommunicationDetailQuery, CommunicationDetailResponseModel>
{
    public async Task<CommunicationDetailResponseModel> Handle(
        GetCommunicationDetailQuery request, CancellationToken ct)
    {
        var communication = await db.Communications
            .AsNoTracking()
            .Where(c => c.Id == request.Id)
            .Select(c => new
            {
                c.Id, c.Channel, c.Flow, c.Subject, c.Body, c.OccurredAt, c.FromAddress,
                c.ThreadId, c.ExternalId, c.PartyType, c.PartyId, c.ContactId,
                c.MatchConfidence, c.IsTriaged, c.DurationMinutes, c.HandledByUserId,
                ContactName = c.Contact == null ? null : c.Contact.LastName + ", " + c.Contact.FirstName,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Communication {request.Id} not found");

        var artifacts = await db.CommunicationArtifacts
            .AsNoTracking()
            .Where(a => a.CommunicationId == request.Id)
            // The raw message first: it is the envelope everything else arrived in.
            .OrderBy(a => a.Kind == CommunicationArtifactKind.Message ? 0 : 1)
            .ThenBy(a => a.Id)
            .Select(a => new CommunicationArtifactResponseModel(
                a.Id,
                a.Kind.ToString(),
                a.Sha256,
                a.ContentType,
                a.ByteSize,
                a.OriginalFilename,
                a.IngestedAt))
            .ToListAsync(ct);

        var links = await db.CommunicationLinks
            .AsNoTracking()
            .Where(l => l.CommunicationId == request.Id)
            .Select(l => new CommunicationLinkResponseModel(l.Id, l.EntityType, l.EntityId))
            .ToListAsync(ct);

        // The party's standing agreements — the "that over there" a purchase
        // order leans on. Party-scoped attestations, so no SalesOrderId.
        var priorAgreements = communication.PartyId is null
            ? []
            : await db.Attestations
                .AsNoTracking()
                .Where(a => a.SalesOrderId == null
                    && a.PartyId == communication.PartyId
                    && a.Status == AcceptanceStatus.Accepted)
                .OrderByDescending(a => a.CapturedAt ?? a.AcceptedAt ?? a.CreatedAt)
                .Select(a => new PriorAgreementResponseModel(
                    a.Id,
                    a.StatementType.ToString(),
                    a.Method.ToString(),
                    a.CapturedAt ?? a.AcceptedAt,
                    a.Artifact == null ? null : a.Artifact.Sha256,
                    a.Artifact == null ? null : a.Artifact.OriginalFilename,
                    a.Note))
                .ToListAsync(ct);

        var thread = string.IsNullOrWhiteSpace(communication.ThreadId)
            ? []
            : await db.Communications
                .AsNoTracking()
                .Where(c => c.ThreadId == communication.ThreadId && c.Id != communication.Id)
                .OrderBy(c => c.OccurredAt)
                .Select(c => new ThreadMessageResponseModel(
                    c.Id, c.Subject, c.FromAddress, c.OccurredAt, c.Flow.ToString()))
                .ToListAsync(ct);

        return new CommunicationDetailResponseModel(
            communication.Id,
            communication.Channel.ToString(),
            communication.Flow.ToString(),
            communication.Subject,
            communication.Body,
            communication.FromAddress,
            communication.OccurredAt,
            communication.DurationMinutes,
            communication.PartyType?.ToString(),
            communication.PartyId,
            communication.ContactId,
            communication.ContactName,
            communication.MatchConfidence?.ToString() ?? "Unmatched",
            communication.IsTriaged,
            communication.HandledByUserId,
            artifacts,
            links,
            priorAgreements,
            thread);
    }
}
