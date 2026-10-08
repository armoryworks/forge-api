using MediatR;

using Forge.Api.Features.Jobs;
using Forge.Api.Services;
using Forge.Core.Enums;

namespace Forge.Api.Features.Mobile;

public record AdvanceJobCommand(
    int JobId, string DeviceKey, string? ScanCode, bool Confirmed = false, int? ConfirmedStageId = null)
    : IRequest<JobAdvanceResponseModel>;

/// <summary>
/// Moves a job to the next column in its track. A duplicate scan (same
/// device, same code, within three seconds) is collapsed to one event: the
/// caller gets the current status back with Collapsed=true and nothing
/// moves twice. A next status that can't be undone or that will queue an
/// accounting document needs Confirmed=true; without it the handler throws
/// <see cref="ConfirmationRequiredException"/> and nothing moves. When the
/// caller also sends ConfirmedStageId, the confirmation only counts while
/// that stage is still the next one, so a job moved by someone else in the
/// meantime is asked about again. The response carries the previous stage
/// so undo can issue the compensating stage move.
/// </summary>
public class AdvanceJobHandler(IMediator mediator, IScanCollapseService collapse)
    : IRequestHandler<AdvanceJobCommand, JobAdvanceResponseModel>
{
    public async Task<JobAdvanceResponseModel> Handle(AdvanceJobCommand request, CancellationToken ct)
    {
        var before = await mediator.Send(new GetJobStatusQuery(request.JobId), ct);

        var action = request.Confirmed ? "advance-confirmed" : "advance";
        if (request.ScanCode is not null
            && collapse.IsDuplicate(request.DeviceKey, request.ScanCode, action))
        {
            return new JobAdvanceResponseModel(
                before, before.PreviousStageId ?? before.StageId, before.PreviousStageName ?? before.StageName, true);
        }

        if (before.NextStageId is null)
            throw new InvalidOperationException("This work order is already at its final status.");

        var confirmed = request.Confirmed
            && (request.ConfirmedStageId is null || request.ConfirmedStageId == before.NextStageId);
        if (!confirmed
            && (before.NextStageIsIrreversible || before.NextStageAccountingDocument is not null))
        {
            throw new ConfirmationRequiredException(ConfirmMessage(before));
        }

        await mediator.Send(new MoveJobStageCommand(request.JobId, before.NextStageId.Value), ct);
        var after = await mediator.Send(new GetJobStatusQuery(request.JobId), ct);

        return new JobAdvanceResponseModel(after, before.StageId, before.StageName, false);
    }

    private static string ConfirmMessage(JobStatusResponseModel status)
    {
        var document = status.NextStageAccountingDocument switch
        {
            AccountingDocumentType.Estimate => "an estimate",
            AccountingDocumentType.SalesOrder => "a sales order",
            AccountingDocumentType.PurchaseOrder => "a purchase order",
            AccountingDocumentType.Invoice => "an invoice",
            AccountingDocumentType.Payment => "a payment",
            _ => null,
        };
        var consequence = (status.NextStageIsIrreversible, document) switch
        {
            (true, not null) => $"can't be undone and queues {document} for your accounting system",
            (true, null) => "can't be undone",
            _ => $"queues {document} for your accounting system",
        };
        return $"Moving {status.JobNumber} to {status.NextStageName} {consequence}. Confirm to continue.";
    }
}
