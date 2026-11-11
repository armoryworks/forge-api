using System.Globalization;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.Operations;
using Forge.Api.Features.Tolerances;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record ReceiveBackSubcontractCommand(int SubcontractOrderId, ReceiveBackRequestModel Data) : IRequest<SubcontractOrderResponseModel>;

public class ReceiveBackSubcontractValidator : AbstractValidator<ReceiveBackSubcontractCommand>
{
    public ReceiveBackSubcontractValidator()
    {
        RuleFor(x => x.Data.ReceivedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.ScrapQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.RejectedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.ReceivedQuantity + x.Data.RejectedQuantity + x.Data.ScrapQuantity)
            .GreaterThan(0)
            .WithName("ReceivedQuantity")
            .WithMessage("Enter a good, rejected or scrap quantity.");
        RuleFor(x => x.Data.ReturnTrackingNumber).MaximumLength(200);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
        RuleFor(x => x.Data.OverToleranceReason).MaximumLength(QuantityToleranceGuard.ReasonMaxLength);
    }
}

public class ReceiveBackSubcontractHandler(
    AppDbContext db,
    IJobOperationService operations,
    ITimerStopService timerStop,
    IHubContext<BoardHub> boardHub,
    IMediator mediator,
    IClock clock,
    IBackflushService? backflush = null,
    IHttpContextAccessor? httpContext = null)
    : IRequestHandler<ReceiveBackSubcontractCommand, SubcontractOrderResponseModel>
{

    public async Task<SubcontractOrderResponseModel> Handle(ReceiveBackSubcontractCommand request, CancellationToken ct)
    {
        var order = await db.SubcontractOrders
            .Include(o => o.Job)
            .Include(o => o.Operation)
            .Include(o => o.Vendor)
            .FirstOrDefaultAsync(o => o.Id == request.SubcontractOrderId, ct)
            ?? throw new KeyNotFoundException($"SubcontractOrder {request.SubcontractOrderId} not found.");

        if (order.ReceivedAt.HasValue || order.Status is SubcontractStatus.Complete or SubcontractStatus.Rejected)
            throw new InvalidOperationException("This subcontract order has already been received back.");

        var tolerance = (await SubcontractTolerances.ForOrdersAsync(
            db, [(order.Id, order.VendorId, order.PurchaseOrderId)], ct))[order.Id];
        var good = request.Data.PassedInspection ? request.Data.ReceivedQuantity : 0m;
        var rejected = request.Data.RejectedQuantity + (request.Data.PassedInspection ? 0m : request.Data.ReceivedQuantity);
        var scrap = request.Data.ScrapQuantity;
        var goodBefore = order.ReceivedQuantity ?? 0m;
        var returnedBefore = goodBefore + order.ScrapQuantity + order.RejectedQuantity;
        var returning = good + rejected + scrap;
        var overReason = QuantityToleranceGuard.EnsureAccepted(
            SubcontractTolerances.Kind, order.Id, order.Quantity, returnedBefore, returning, tolerance,
            request.Data.ConfirmOverTolerance, request.Data.OverToleranceReason, httpContext?.HttpContext?.User, "receive back",
            string.Format(CultureInfo.InvariantCulture, "the {0:0.####} sent for Op {1} {2}", order.Quantity, order.Operation.StepNumber, order.Operation.Title));

        var row = good + scrap > 0m
            ? await FindOrCreateOperationRowAsync(order, ct)
            : null;
        var jobQuantity = row is null ? 0m : await operations.GetJobQuantityAsync(order.Job, ct);
        var stepGood = (row?.CompletedQuantity ?? 0m) + good;
        if (row is not null && good > 0m && stepGood > jobQuantity
            && await operations.GetOverrunLimitAsync(jobQuantity, ct) is decimal limit && stepGood > limit)
            throw JobOverrun.LimitExceeded(nameof(request.Data.ReceivedQuantity), stepGood, jobQuantity, limit);

        var now = clock.UtcNow;
        var previousStatus = order.Status;
        var returnedTotal = returnedBefore + returning;
        var withinTolerance = tolerance.Completes(order.Quantity, returnedTotal);
        var closes = withinTolerance || request.Data.CloseShort;
        order.ReceivedQuantity = goodBefore + good;
        order.RejectedQuantity += rejected;
        order.ScrapQuantity += scrap;
        order.ReturnTrackingNumber = request.Data.ReturnTrackingNumber?.Trim() ?? order.ReturnTrackingNumber;
        order.Notes = request.Data.Notes?.Trim() ?? order.Notes;
        if (overReason is not null)
            order.OverToleranceReason = overReason;
        if (closes)
        {
            order.ReceivedAt = now;
            order.ReceivedById = db.CurrentUserId;
        }
        order.Status = !closes ? SubcontractStatus.PartiallyReceived
            : order.ReceivedQuantity > 0m ? SubcontractStatus.Complete
            : SubcontractStatus.Rejected;

        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = order.JobId,
            UserId = db.CurrentUserId,
            Action = ActivityAction.StatusChanged,
            FieldName = "Subcontract",
            OldValue = previousStatus.ToString(),
            NewValue = order.Status.ToString(),
            OperationId = order.OperationId,
            CreatedAt = now,
            Description = $"Received back {good:0.####} good, {(rejected > 0m ? $"{rejected:0.####} rejected, " : string.Empty)}{scrap:0.####} scrap from {order.Vendor.CompanyName} for Op {order.Operation.StepNumber} {order.Operation.Title}"
                + DescribeOutcome(order, returnedTotal, withinTolerance, overReason),
        });

        var advance = row is not null
            ? await AdvanceOperationAsync(order, row, good, scrap, jobQuantity, now, ct)
            : null;

        var runBackflush = backflush is not null && row?.Status == JobOperationStatus.Complete
            && db.CurrentUserId.HasValue;
        await using var tx = runBackflush && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(JobOperationRules.StaleMessage);
        }

        if (runBackflush)
            await backflush!.BackflushJobAsync(order.JobId, db.CurrentUserId!.Value, now, ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        if (advance is not null)
        {
            if (advance.Count > 0)
                await timerStop.PublishStoppedAsync(advance, ct);
            await JobOperationRules.BroadcastJobUpdatedAsync(boardHub, mediator, order.Job, ct);
        }

        var poNumber = order.PurchaseOrderId.HasValue
            ? await db.PurchaseOrders.Where(p => p.Id == order.PurchaseOrderId).Select(p => p.PONumber).FirstOrDefaultAsync(ct)
            : null;

        return new SubcontractOrderResponseModel(
            order.Id, order.JobId, order.Job.JobNumber ?? $"J-{order.Job.Id}",
            order.OperationId, order.Operation.Title,
            order.VendorId, order.Vendor.CompanyName,
            order.PurchaseOrderId, poNumber,
            order.Quantity, order.UnitCost, order.Quantity * order.UnitCost,
            order.SentAt, order.ExpectedReturnDate, order.ReceivedAt,
            order.ReceivedQuantity, order.Status.ToString(),
            order.ShippingTrackingNumber, order.ReturnTrackingNumber, order.Notes,
            order.CreatedAt,
            order.ScrapQuantity,
            tolerance.OverPercent,
            tolerance.UnderPercent,
            tolerance.MaxAccepted(order.Quantity),
            tolerance.MinComplete(order.Quantity),
            order.OverToleranceReason,
            order.RejectedQuantity);
    }

    private static string DescribeOutcome(SubcontractOrder order, decimal returnedTotal, bool withinTolerance, string? overReason)
    {
        var outcome = order.Status switch
        {
            SubcontractStatus.PartiallyReceived => string.Format(CultureInfo.InvariantCulture,
                "; {0:0.####} of {1:0.####} back, the rest stays open", returnedTotal, order.Quantity),
            SubcontractStatus.Complete or SubcontractStatus.Rejected when returnedTotal < order.Quantity => withinTolerance
                ? string.Format(CultureInfo.InvariantCulture, "; {0:0.####} of {1:0.####} back, closed within tolerance", returnedTotal, order.Quantity)
                : string.Format(CultureInfo.InvariantCulture, "; {0:0.####} of {1:0.####} back, closed short", returnedTotal, order.Quantity),
            _ => string.Empty,
        };
        return overReason is null ? outcome : $"{outcome}; accepted over tolerance: {overReason}";
    }

    private async Task<JobOperation?> FindOrCreateOperationRowAsync(SubcontractOrder order, CancellationToken ct)
    {
        var job = order.Job;
        if (job.CompletedDate.HasValue || job.IsArchived || job.Disposition.HasValue)
            return null;
        if (!await operations.IsTrackingEnabledAsync(ct))
            return null;

        var row = await operations.EnsureRowAsync(job, order.Operation, ct);
        return JobOperationRules.IsClosed(row.Status) ? null : row;
    }

    private async Task<List<TimeEntry>> AdvanceOperationAsync(
        SubcontractOrder order, JobOperation row, decimal good, decimal scrap, decimal jobQuantity, DateTimeOffset now, CancellationToken ct)
    {
        var before = JobOperationRules.Describe(row, jobQuantity);

        row.CompletedQuantity += good;
        row.ScrapQuantity += scrap;
        row.StartedAt ??= now;

        var reached = row.CompletedQuantity + row.ScrapQuantity >= jobQuantity;
        var closed = new List<TimeEntry>();
        if (reached)
        {
            row.Status = JobOperationStatus.Complete;
            row.CompletedAt = now;
            row.CompletedById = db.CurrentUserId;

            var openTimers = await db.TimeEntries
                .Where(t => t.JobOperationId == row.Id && t.TimerStart != null && t.TimerStop == null)
                .ToListAsync(ct);
            if (openTimers.Count > 0)
            {
                var actorName = await db.Users
                    .Where(u => u.Id == db.CurrentUserId)
                    .Select(u => u.LastName + ", " + u.FirstName)
                    .FirstOrDefaultAsync(ct) ?? "another user";
                foreach (var timer in openTimers)
                {
                    timerStop.Close(timer, now,
                        reason: "operation completed",
                        appendNote: $"stopped: operation completed by {actorName}");
                    closed.Add(timer);
                }
            }
        }
        else
        {
            row.Status = JobOperationStatus.InProgress;
        }

        var after = JobOperationRules.Describe(row, jobQuantity);
        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = order.JobId,
            UserId = db.CurrentUserId,
            Action = reached ? ActivityAction.OperationCompleted : ActivityAction.OperationProgress,
            FieldName = "OperationStatus",
            OldValue = before,
            NewValue = after,
            Description = $"Operation {order.Operation.StepNumber} {order.Operation.Title}: {before} → {after} (received back from {order.Vendor.CompanyName})."
                + (good > 0m ? JobOverrun.Note(jobQuantity, row.CompletedQuantity) : string.Empty),
            CreatedAt = now,
            OperationId = order.OperationId,
            WorkCenterId = order.Operation.WorkCenterId,
        });

        return closed;
    }
}
