using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.IoT;

public record GetMachineConnectionsQuery(bool? IsActive) : IRequest<List<MachineConnectionResponseModel>>;

public class GetMachineConnectionsHandler(AppDbContext db)
    : IRequestHandler<GetMachineConnectionsQuery, List<MachineConnectionResponseModel>>
{
    public async Task<List<MachineConnectionResponseModel>> Handle(
        GetMachineConnectionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.MachineConnections
            .AsNoTracking()
            .Include(c => c.WorkCenter)
            .Include(c => c.Tags)
            .AsQueryable();

        if (request.IsActive.HasValue)
            query = query.Where(c => c.IsActive == request.IsActive.Value);

        var connections = await query
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var tagIds = connections.SelectMany(c => c.Tags).Select(t => t.Id).ToList();
        var lastByTag = await db.MachineDataPoints
            .AsNoTracking()
            .Where(p => tagIds.Contains(p.TagId))
            .GroupBy(p => p.TagId)
            .Select(g => new { TagId = g.Key, Last = g.Max(p => p.Timestamp) })
            .ToDictionaryAsync(x => x.TagId, x => x.Last, cancellationToken);

        return connections.Select(c => new MachineConnectionResponseModel
        {
            Id = c.Id,
            WorkCenterId = c.WorkCenterId,
            WorkCenterName = c.WorkCenter.Name,
            Name = c.Name,
            OpcUaEndpoint = c.OpcUaEndpoint,
            SecurityPolicy = c.SecurityPolicy,
            AuthType = c.AuthType,
            Status = c.Status,
            LastConnectedAt = c.LastConnectedAt,
            LastError = c.LastError,
            PollIntervalMs = c.PollIntervalMs,
            IsActive = c.IsActive,
            TagCount = c.Tags.Count(t => t.IsActive),
            CredentialsConfigured = !string.IsNullOrEmpty(c.EncryptedCredentials),
            LastDataPointAt = c.Tags
                .Where(t => lastByTag.ContainsKey(t.Id))
                .Select(t => (DateTimeOffset?)lastByTag[t.Id])
                .Max(),
            Tags = c.Tags.Select(t => new MachineTagResponseModel
            {
                Id = t.Id,
                TagName = t.TagName,
                OpcNodeId = t.OpcNodeId,
                DataType = t.DataType,
                Unit = t.Unit,
                WarningThresholdLow = t.WarningThresholdLow,
                WarningThresholdHigh = t.WarningThresholdHigh,
                AlarmThresholdLow = t.AlarmThresholdLow,
                AlarmThresholdHigh = t.AlarmThresholdHigh,
                IsActive = t.IsActive,
            }).ToList(),
        }).ToList();
    }
}
