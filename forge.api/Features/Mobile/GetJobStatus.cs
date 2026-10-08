using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record GetJobStatusQuery(int JobId) : IRequest<JobStatusResponseModel>;

/// <summary>
/// The phone's job card: identity, where it is, where it goes next (and
/// where it came from, for undo), and the last three timeline entries.
/// Next and previous skip statuses hidden for the job's order type, so a
/// scan never aims at a column the board doesn't show.
/// The next status's accounting document is reported only when moving
/// there will queue one: an external accounting provider is active and the
/// job's customer is linked to it.
/// </summary>
public class GetJobStatusHandler(
    AppDbContext db, IMediator mediator, IClock clock, IAccountingProviderFactory accountingProviders)
    : IRequestHandler<GetJobStatusQuery, JobStatusResponseModel>
{
    public async Task<JobStatusResponseModel> Handle(GetJobStatusQuery request, CancellationToken ct)
    {
        var job = await mediator.Send(new GetJobByIdQuery(request.JobId), ct);

        var stages = await db.JobStages.AsNoTracking()
            .Where(s => s.TrackTypeId == job.TrackTypeId && (s.IsActive || s.Id == job.CurrentStageId))
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id, s.Name, s.IsShopFloor, s.IsIrreversible, s.AccountingDocumentType })
            .ToListAsync(ct);

        var index = stages.FindIndex(s => s.Id == job.CurrentStageId);
        var next = index >= 0 && index + 1 < stages.Count ? stages[index + 1] : null;
        var previous = index > 0 ? stages[index - 1] : null;

        var activity = (await mediator.Send(new GetJobActivityQuery(request.JobId), ct))
            .OrderByDescending(a => a.CreatedAt)
            .Take(3)
            .ToList();

        var accountingDocument = next?.AccountingDocumentType is not null
            && await QueuesAccountingDocumentAsync(job.CustomerId, ct)
                ? next.AccountingDocumentType
                : null;

        var startOfToday = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        return new JobStatusResponseModel(
            job.Id, job.JobNumber, job.Title, job.CustomerName,
            job.CurrentStageId, job.StageName, job.StageColor,
            job.DueDate, job.DueDate is not null && job.DueDate < startOfToday && job.CompletedDate is null,
            next?.Id, next?.Name, previous?.Id, previous?.Name,
            job.RowVersion, activity)
        {
            NextStageIsShopFloor = next?.IsShopFloor ?? false,
            NextStageIsIrreversible = next?.IsIrreversible ?? false,
            NextStageAccountingDocument = accountingDocument,
        };
    }

    private async Task<bool> QueuesAccountingDocumentAsync(int? customerId, CancellationToken ct)
    {
        if (customerId is null) return false;

        var providerId = await accountingProviders.GetActiveProviderIdAsync(ct);
        if (providerId is null or "local") return false;

        return await db.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == customerId && c.ExternalId != null && c.ExternalId != "", ct);
    }
}
