using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record TrainingScanLogResponseModel(
    int Id,
    int UserId,
    string UserName,
    string ActionType,
    int? PartId,
    string? PartNumber,
    int? FromLocationId,
    string? FromLocation,
    int? ToLocationId,
    string? ToLocation,
    int Quantity,
    int? JobId,
    string? JobNumber,
    int? PurchaseOrderId,
    int? ShipmentId,
    DateTimeOffset ScannedAt,
    bool WasSuccessful,
    string? ErrorMessage);

public record GetTrainingLogQuery(int? UserId, DateOnly? Date) : IRequest<List<TrainingScanLogResponseModel>>;

public class GetTrainingLogHandler(AppDbContext db)
    : IRequestHandler<GetTrainingLogQuery, List<TrainingScanLogResponseModel>>
{
    public async Task<List<TrainingScanLogResponseModel>> Handle(GetTrainingLogQuery request, CancellationToken ct)
    {
        var query = db.TrainingScanLogs.AsNoTracking().AsQueryable();

        if (request.UserId.HasValue)
            query = query.Where(l => l.UserId == request.UserId.Value);

        if (request.Date.HasValue)
        {
            var start = request.Date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = start.AddDays(1);
            query = query.Where(l => l.ScannedAt >= start && l.ScannedAt < end);
        }

        return await query
            .OrderByDescending(l => l.ScannedAt)
            .Take(200)
            .Select(l => new TrainingScanLogResponseModel(
                l.Id,
                l.UserId,
                db.Users.Where(u => u.Id == l.UserId).Select(u => u.LastName + ", " + u.FirstName).FirstOrDefault() ?? "",
                l.ActionType.ToString(),
                l.PartId,
                l.PartId != null ? db.Parts.Where(p => p.Id == l.PartId).Select(p => p.PartNumber).FirstOrDefault() : null,
                l.FromLocationId,
                l.FromLocationId != null ? db.StorageLocations.Where(s => s.Id == l.FromLocationId).Select(s => s.Name).FirstOrDefault() : null,
                l.ToLocationId,
                l.ToLocationId != null ? db.StorageLocations.Where(s => s.Id == l.ToLocationId).Select(s => s.Name).FirstOrDefault() : null,
                l.Quantity,
                l.JobId,
                l.JobId != null ? db.Jobs.Where(j => j.Id == l.JobId).Select(j => j.JobNumber).FirstOrDefault() : null,
                l.PurchaseOrderId,
                l.ShipmentId,
                l.ScannedAt,
                l.WasSuccessful,
                l.ErrorMessage))
            .ToListAsync(ct);
    }
}
