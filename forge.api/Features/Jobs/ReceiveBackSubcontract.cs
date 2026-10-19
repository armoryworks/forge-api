using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.Operations;
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
        RuleFor(x => x.Data.ReceivedQuantity + x.Data.ScrapQuantity)
            .GreaterThan(0)
            .WithName("ReceivedQuantity")
            .WithMessage("Enter a good or scrap quantity.");
        RuleFor(x => x.Data.ReturnTrackingNumber).MaximumLength(200);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
    }
}

public class ReceiveBackSubcontractHandler(
    AppDbContext db,
    IJobOperationService operations,
    ITimerStopService timerStop,
    IHubContext<BoardHub> boardHub,
    IMediator mediator,
    IClock clock,
    IBackflushService? backflush = null)
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

        var row = request.Data.PassedInspection
            ? await FindOrCreateOperationRowAsync(order, ct)
            : null;

        var now = clock.UtcNow;
        var previousStatus = order.Status;
        order.ReceivedAt = now;
        order.ReceivedById = db.CurrentUserId;
        order.ReceivedQuantity = request.Data.ReceivedQuantity;
        order.ReturnTrackingNumber = request.Data.ReturnTrackingNumber?.Trim();
        order.Notes = request.Data.Notes?.Trim() ?? order.Notes;
        order.Status = request.Data.PassedInspection
            ? SubcontractStatus.Complete
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
            Description = $"Received back {request.Data.ReceivedQuantity:0.####} good, {request.Data.ScrapQuantity:0.####} scrap from {order.Vendor.CompanyName} for Op {order.Operation.StepNumber} {order.Operation.Title}",
        });

        var advance = row is not null
            ? await AdvanceOperationAsync(order, row, request.Data, now, ct)
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
            order.CreatedAt);
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
        SubcontractOrder order, JobOperation row, ReceiveBackRequestModel data, DateTimeOffset now, CancellationToken ct)
    {
        var job = order.Job;
        var jobQuantity = await operations.GetJobQuantityAsync(job, ct);
        var before = JobOperationRules.Describe(row, jobQuantity);

        var remaining = Math.Max(0m, jobQuantity - row.CompletedQuantity - row.ScrapQuantity);
        var scrapAdded = Math.Min(data.ScrapQuantity, remaining);
        var goodAdded = Math.Min(data.ReceivedQuantity, remaining - scrapAdded);
        var notCounted = data.ReceivedQuantity + data.ScrapQuantity - goodAdded - scrapAdded;

        row.CompletedQuantity += goodAdded;
        row.ScrapQuantity += scrapAdded;
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
        var overNote = notCounted > 0m
            ? $"; {notCounted:0.####} over the job quantity not counted"
            : string.Empty;
        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = order.JobId,
            UserId = db.CurrentUserId,
            Action = reached ? ActivityAction.OperationCompleted : ActivityAction.OperationProgress,
            FieldName = "OperationStatus",
            OldValue = before,
            NewValue = after,
            Description = $"Operation {order.Operation.StepNumber} {order.Operation.Title}: {before} → {after} (received back from {order.Vendor.CompanyName}{overNote}).",
            CreatedAt = now,
            OperationId = order.OperationId,
            WorkCenterId = order.Operation.WorkCenterId,
        });

        return closed;
    }
}
