using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>Shared read + mapping for acceptance records (list, by-id) — joins recorder + file names.</summary>
internal static class AcceptanceQuery
{
    public static async Task<SalesOrderAcceptanceResponseModel> ByIdAsync(AppDbContext db, int id, CancellationToken ct)
    {
        var rows = await LoadAsync(db, db.SalesOrderAcceptances.AsNoTracking().Where(a => a.Id == id), ct);
        return rows.Single();
    }

    public static Task<List<SalesOrderAcceptanceResponseModel>> ForSalesOrderAsync(AppDbContext db, int salesOrderId, CancellationToken ct)
        => LoadAsync(db, db.SalesOrderAcceptances.AsNoTracking()
            .Where(a => a.SalesOrderId == salesOrderId)
            .OrderByDescending(a => a.CreatedAt), ct);

    private static async Task<List<SalesOrderAcceptanceResponseModel>> LoadAsync(
        AppDbContext db, IQueryable<SalesOrderAcceptance> source, CancellationToken ct)
    {
        // Materialize entity + joined display names, then map in memory (enum→string is client-side).
        var rows = await (
            from a in source
            join u in db.Users on a.RecordedByUserId equals u.Id into us
            from u in us.DefaultIfEmpty()
            join f in db.FileAttachments on a.FileAttachmentId equals f.Id into fs
            from f in fs.DefaultIfEmpty()
            select new
            {
                Acceptance = a,
                RecorderFirst = u != null ? u.FirstName : null,
                RecorderLast = u != null ? u.LastName : null,
                FileName = f != null ? f.FileName : null,
            }).ToListAsync(ct);

        return rows.Select(r =>
        {
            var a = r.Acceptance;
            var recorder = string.IsNullOrWhiteSpace(r.RecorderFirst) && string.IsNullOrWhiteSpace(r.RecorderLast)
                ? null
                : $"{r.RecorderLast}, {r.RecorderFirst}".Trim().Trim(',').Trim();
            return new SalesOrderAcceptanceResponseModel(
                a.Id, a.SalesOrderId, a.Status.ToString(), a.Method.ToString(),
                a.FileAttachmentId, r.FileName,
                a.RecordedByUserId, recorder,
                a.AcceptedByName, a.Provider, a.ProviderReference, a.SentTo, a.Note,
                a.ExpiresAt, a.AcceptedAt, a.CreatedAt);
        }).ToList();
    }
}
