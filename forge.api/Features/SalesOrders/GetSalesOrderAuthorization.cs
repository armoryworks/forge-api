using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models.Communications;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// The Authorized-by line: what authorized this order, when it arrived, from
/// whom, and its hash — with routes through to the original message, the
/// document itself, and the agreement chain behind it.
///
/// <para>Returns null rather than throwing when an order has no authorizing
/// attestation. Most orders will not have one — they were keyed in, or converted
/// from a quote — and the absence is information, not an error.</para>
/// </summary>
public record GetSalesOrderAuthorizationQuery(int SalesOrderId)
    : IRequest<SalesOrderAuthorizationResponseModel?>;

public class GetSalesOrderAuthorizationHandler(AppDbContext db)
    : IRequestHandler<GetSalesOrderAuthorizationQuery, SalesOrderAuthorizationResponseModel?>
{
    public async Task<SalesOrderAuthorizationResponseModel?> Handle(
        GetSalesOrderAuthorizationQuery request, CancellationToken ct)
    {
        var attestationId = await db.SalesOrders
            .AsNoTracking()
            .Where(o => o.Id == request.SalesOrderId)
            .Select(o => o.AuthorizingAttestationId)
            .FirstOrDefaultAsync(ct);

        if (attestationId is not int id) return null;

        var row = await db.Attestations
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.StatementType,
                a.Method,
                a.Status,
                a.CapturedAt,
                a.AcceptedAt,
                a.SupportedByAttestationId,
                a.CommunicationId,
                ArtifactId = a.ArtifactId,
                Sha256 = a.Artifact == null ? null : a.Artifact.Sha256,
                Filename = a.Artifact == null ? null : a.Artifact.OriginalFilename,
                FromAddress = a.Communication == null ? null : a.Communication.FromAddress,
                Channel = a.Communication == null ? null : a.Communication.Channel.ToString(),
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        // Walk the chain the reviewer identified. Bounded rather than recursive:
        // a supersession or support chain more than a few links deep is a data
        // problem, and an unbounded walk would turn one into a hang.
        var chain = new List<PriorAgreementResponseModel>();
        var nextId = row.SupportedByAttestationId;
        for (var depth = 0; depth < 8 && nextId is int linkId; depth++)
        {
            var link = await db.Attestations
                .AsNoTracking()
                .Where(a => a.Id == linkId)
                .Select(a => new
                {
                    a.Id, a.StatementType, a.Method, a.SupportedByAttestationId,
                    Captured = a.CapturedAt ?? a.AcceptedAt,
                    Sha = a.Artifact == null ? null : a.Artifact.Sha256,
                    File = a.Artifact == null ? null : a.Artifact.OriginalFilename,
                    a.Note,
                })
                .FirstOrDefaultAsync(ct);

            if (link is null) break;

            chain.Add(new PriorAgreementResponseModel(
                link.Id, link.StatementType.ToString(), link.Method.ToString(),
                link.Captured, link.Sha, link.File, link.Note));

            nextId = link.SupportedByAttestationId;
        }

        return new SalesOrderAuthorizationResponseModel(
            row.Id,
            row.StatementType.ToString(),
            row.Method.ToString(),
            row.Status.ToString(),
            // The moment the party stated it — not when staff got to it. This is
            // the timestamp the line quotes.
            row.CapturedAt ?? row.AcceptedAt,
            row.FromAddress,
            row.Channel,
            row.ArtifactId,
            row.Filename,
            row.Sha256,
            row.CommunicationId,
            chain);
    }
}
