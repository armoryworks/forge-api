using System.Globalization;

using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public sealed record SubcontractSendRoom(
    decimal StartQuantity, decimal? Limit, IReadOnlyDictionary<int, decimal> Committed, IReadOnlyDictionary<int, decimal> Scrap)
{
    public decimal CommittedFor(int operationId) => Committed.GetValueOrDefault(operationId);

    public decimal RemainingFor(int operationId) =>
        Math.Max(0m, StartQuantity - CommittedFor(operationId) - Scrap.GetValueOrDefault(operationId));

    public decimal? MaxSendFor(int operationId) =>
        Limit is decimal limit ? Math.Max(0m, limit - CommittedFor(operationId)) : null;
}

public static class SubcontractSendLimit
{
    public const string Code = "subcontract-send-over-limit";

    public static bool IsOpen(SubcontractStatus status, DateTimeOffset? receivedAt) =>
        receivedAt is null && status is not (SubcontractStatus.Complete or SubcontractStatus.Rejected);

    public static async Task<SubcontractSendRoom> ForJobAsync(
        AppDbContext db, IJobOperationService operations, Job job, CancellationToken ct)
    {
        var startQuantity = await operations.GetJobQuantityAsync(job, ct);
        var limit = await operations.GetOverrunLimitAsync(startQuantity, ct);

        var orders = await db.SubcontractOrders.AsNoTracking()
            .Where(o => o.JobId == job.Id)
            .Select(o => new
            {
                o.OperationId, o.Quantity, Good = o.ReceivedQuantity ?? 0m, o.ScrapQuantity, o.RejectedQuantity, o.Status, o.ReceivedAt,
            })
            .ToListAsync(ct);
        var steps = await operations.IsTrackingEnabledAsync(ct)
            ? (await db.JobOperations.AsNoTracking()
                    .Where(r => r.JobId == job.Id && r.OperationId != null)
                    .Select(r => new { OperationId = r.OperationId!.Value, r.CompletedQuantity, r.ScrapQuantity })
                    .ToListAsync(ct))
                .GroupBy(r => r.OperationId)
                .ToDictionary(g => g.Key, g => (Good: g.Sum(r => r.CompletedQuantity), Scrap: g.Sum(r => r.ScrapQuantity)))
            : [];

        var committed = new Dictionary<int, decimal>();
        var scrap = new Dictionary<int, decimal>();
        foreach (var operationId in orders.Select(o => o.OperationId).Concat(steps.Keys).Distinct())
        {
            var sent = orders.Where(o => o.OperationId == operationId).ToList();
            var step = steps.TryGetValue(operationId, out var row) ? row : (Good: sent.Sum(o => o.Good), Scrap: sent.Sum(o => o.ScrapQuantity));
            committed[operationId] = step.Good + sent
                .Where(o => IsOpen(o.Status, o.ReceivedAt))
                .Sum(o => Math.Max(0m, o.Quantity - o.Good - o.ScrapQuantity - o.RejectedQuantity));
            scrap[operationId] = step.Scrap;
        }

        return new SubcontractSendRoom(startQuantity, limit, committed, scrap);
    }

    public static ValidationException Exceeded(string property, decimal quantity, int operationId, SubcontractSendRoom room)
    {
        var max = room.MaxSendFor(operationId) ?? 0m;
        return new ValidationException([
            new ValidationFailure(property, string.Format(CultureInfo.InvariantCulture,
                "Cannot send {0:0.####} out: at most {1:0.####} more can go out on this step. {2:0.####} is already done or "
                    + "out, and the job allows {3:0.####} against a start quantity of {4:0.####}.",
                quantity, max, room.CommittedFor(operationId), room.Limit, room.StartQuantity))
            {
                ErrorCode = Code,
                FormattedMessagePlaceholderValues = new Dictionary<string, object?> { ["ComparisonValue"] = max },
            },
        ]);
    }
}
