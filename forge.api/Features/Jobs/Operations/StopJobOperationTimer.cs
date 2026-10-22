using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Operations;

public record StopJobOperationTimerCommand(int JobId, int OperationId, StopJobOperationTimerRequestModel Data)
    : IRequest<JobOperationTimerStopResponseModel>;

public class StopJobOperationTimerValidator : AbstractValidator<StopJobOperationTimerCommand>
{
    public StopJobOperationTimerValidator()
    {
        RuleFor(x => x.Data.GoodQuantity).GreaterThanOrEqualTo(0m).JobQuantityLimits().When(x => x.Data.GoodQuantity.HasValue);
        RuleFor(x => x.Data.ScrapQuantity).GreaterThanOrEqualTo(0m).JobQuantityLimits().When(x => x.Data.ScrapQuantity.HasValue);
        RuleFor(x => x.Data.ReasonCode).MaximumLength(50);
        RuleFor(x => x.Data.Notes).MaximumLength(1000);
    }
}

public class StopJobOperationTimerHandler(
    ITimeTrackingRepository repo,
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IMediator mediator,
    IClock clock) : IRequestHandler<StopJobOperationTimerCommand, JobOperationTimerStopResponseModel>
{
    public async Task<JobOperationTimerStopResponseModel> Handle(StopJobOperationTimerCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var data = request.Data;

        if (!await db.Jobs.AnyAsync(j => j.Id == request.JobId, cancellationToken))
            throw new KeyNotFoundException($"Job {request.JobId} not found");

        var open = await FindOpenTimerAsync(userId, request, cancellationToken);
        var recordsProgress = data.GoodQuantity > 0m || data.ScrapQuantity > 0m || data.MarkComplete;
        if (open is null && !recordsProgress)
            return new JobOperationTimerStopResponseModel(false, null);

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        JobOperationRowResponseModel? operation = null;
        if (recordsProgress)
        {
            var row = await db.JobOperations.AsNoTracking()
                .Where(r => r.JobId == request.JobId && r.OperationId == request.OperationId)
                .Select(r => new { r.CompletedQuantity, r.ScrapQuantity })
                .FirstOrDefaultAsync(cancellationToken);
            var progress = await mediator.Send(new UpdateJobOperationProgressCommand(
                request.JobId,
                request.OperationId,
                new UpdateJobOperationProgressRequestModel(
                    data.GoodQuantity > 0m ? (row?.CompletedQuantity ?? 0m) + data.GoodQuantity.Value : null,
                    data.ScrapQuantity > 0m ? (row?.ScrapQuantity ?? 0m) + data.ScrapQuantity.Value : null,
                    data.MarkComplete ? JobOperationStatus.Complete : null,
                    ReasonCode: data.ReasonCode)), cancellationToken);
            operation = progress.Operation;
        }

        StoppedTimerResponseModel? stopped = null;
        if (open is int timeEntryId && await IsStillOpenAsync(timeEntryId, cancellationToken))
            stopped = await mediator.Send(
                new StopActiveTimerCommand(userId, clock.UtcNow, data.Notes, TimeEntryId: timeEntryId),
                cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        var entryId = stopped?.TimeEntryId ?? open;
        return new JobOperationTimerStopResponseModel(
            open is not null,
            entryId is int id ? await repo.GetTimeEntryByIdAsync(id, cancellationToken) : null,
            operation);
    }

    private Task<int?> FindOpenTimerAsync(int userId, StopJobOperationTimerCommand request, CancellationToken cancellationToken)
        => db.TimeEntries
            .Where(t => t.UserId == userId && t.JobId == request.JobId && t.OperationId == request.OperationId
                && t.JobOperationId != null && t.TimerStart != null && t.TimerStop == null)
            .OrderByDescending(t => t.TimerStart)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<bool> IsStillOpenAsync(int timeEntryId, CancellationToken cancellationToken)
        => db.TimeEntries.AnyAsync(t => t.Id == timeEntryId && t.TimerStop == null, cancellationToken);
}
