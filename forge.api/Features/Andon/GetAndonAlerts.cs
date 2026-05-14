using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Andon;

public record GetAndonAlertsQuery(int? WorkCenterId, AndonAlertStatus? Status) : IRequest<List<AndonAlertResponseModel>>;

public class GetAndonAlertsHandler(AppDbContext db, UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetAndonAlertsQuery, List<AndonAlertResponseModel>>
{
    public async Task<List<AndonAlertResponseModel>> Handle(
        GetAndonAlertsQuery request, CancellationToken cancellationToken)
    {
        var query = db.AndonAlerts
            .AsNoTracking()
            .Include(a => a.WorkCenter)
            .Include(a => a.Job)
            .AsQueryable();

        if (request.WorkCenterId.HasValue)
            query = query.Where(a => a.WorkCenterId == request.WorkCenterId.Value);

        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);

        var alerts = await query
            .OrderByDescending(a => a.RequestedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var userIds = alerts
            .SelectMany(a => new[] { a.RequestedById, a.AcknowledgedById ?? 0, a.ResolvedById ?? 0 })
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var users = await userManager.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.LastName, u.FirstName })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        string GetName(int id) =>
            users.TryGetValue(id, out var u) ? $"{u.LastName}, {u.FirstName}" : string.Empty;

        return alerts.Select(a =>
        {
            var responseTime = a.AcknowledgedAt.HasValue
                ? (decimal?)(a.AcknowledgedAt.Value - a.RequestedAt).TotalMinutes
                : null;
            var resolutionTime = a.ResolvedAt.HasValue
                ? (decimal?)(a.ResolvedAt.Value - a.RequestedAt).TotalMinutes
                : null;

            return new AndonAlertResponseModel
            {
                Id = a.Id,
                WorkCenterId = a.WorkCenterId,
                WorkCenterName = a.WorkCenter.Name,
                Type = a.Type,
                Status = a.Status,
                RequestedByName = GetName(a.RequestedById),
                RequestedAt = a.RequestedAt,
                AcknowledgedByName = a.AcknowledgedById.HasValue ? GetName(a.AcknowledgedById.Value) : null,
                AcknowledgedAt = a.AcknowledgedAt,
                ResolvedByName = a.ResolvedById.HasValue ? GetName(a.ResolvedById.Value) : null,
                ResolvedAt = a.ResolvedAt,
                ResponseTimeMinutes = responseTime.HasValue ? Math.Round(responseTime.Value, 1) : null,
                ResolutionTimeMinutes = resolutionTime.HasValue ? Math.Round(resolutionTime.Value, 1) : null,
                Notes = a.Notes,
                JobId = a.JobId,
                JobNumber = a.Job?.JobNumber,
            };
        }).ToList();
    }
}
