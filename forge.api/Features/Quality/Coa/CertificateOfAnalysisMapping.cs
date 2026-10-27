using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>Projects certificates of analysis to their response shape, newest first.</summary>
public static class CertificateOfAnalysisMapping
{
    public static async Task<List<CertificateOfAnalysisResponseModel>> ListAsync(
        AppDbContext db, IQueryable<CertificateOfAnalysis> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .OrderByDescending(c => c.GeneratedAt).ThenByDescending(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.CertificateNumber,
                c.LotRecordId,
                c.LotRecord.LotNumber,
                c.PartId,
                c.Part.PartNumber,
                Revision = c.LotRecord.PartRevision ?? c.Part.Revision,
                c.CustomerId,
                CustomerName = c.Customer != null ? c.Customer.Name : null,
                c.ShipmentId,
                ShipmentNumber = c.Shipment != null ? c.Shipment.ShipmentNumber : null,
                c.Status,
                c.Conforms,
                c.Language,
                c.GeneratedAt,
                c.GeneratedById,
                c.FileAttachmentId,
                c.Notes,
                Results = c.Results.OrderBy(r => r.SortOrder).ToList(),
            })
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.GeneratedById != null).Select(r => r.GeneratedById!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct);

        return rows.Select(r => new CertificateOfAnalysisResponseModel(
                r.Id, r.CertificateNumber, r.LotRecordId, r.LotNumber, r.PartId, r.PartNumber, r.Revision,
                r.CustomerId, r.CustomerName, r.ShipmentId, r.ShipmentNumber, r.Status, r.Conforms, r.Language,
                r.GeneratedAt,
                r.GeneratedById is int uid ? names.GetValueOrDefault(uid) : null,
                r.FileAttachmentId, r.Notes,
                r.Results.Select(x => new CoaResultResponseModel(
                    x.Id, x.SortOrder, x.Characteristic, x.Specification, x.SpecMin, x.SpecMax, x.SpecTarget,
                    x.Result, x.Unit, x.Method, x.Pass, x.SourceType, x.SourceId)).ToList()))
            .ToList();
    }
}
