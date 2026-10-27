using System.Globalization;

using MediatR;

using Forge.Api.Features.VendorCredits;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentRuns;

public record ResumePaymentRunCommand(int Id) : IRequest<PaymentRunDetailResponseModel>;

/// <summary>
/// Resumes an interrupted (or never finished) payment run: pays only the vendors not yet paid
/// (<c>payment-run-not-resumable</c> for a completed or cancelled run).
/// </summary>
public class ResumePaymentRunHandler(AppDbContext db, PaymentRunExecutor executor, IMediator mediator)
    : IRequestHandler<ResumePaymentRunCommand, PaymentRunDetailResponseModel>
{
    public const string NotResumableCode = "payment-run-not-resumable";

    public async Task<PaymentRunDetailResponseModel> Handle(ResumePaymentRunCommand request, CancellationToken ct)
    {
        var run = await db.PaymentRuns.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Payment run {request.Id} not found");
        if (run.Status is PaymentRunStatus.Completed or PaymentRunStatus.Cancelled)
            throw VendorCreditMemoRules.Failure("Id", NotResumableCode, $"Payment run {run.RunNumber} is {run.Status}; there is nothing left to pay.");

        db.LogActivityAt("payment-run-resumed", string.Create(CultureInfo.InvariantCulture, $"Payment run {run.RunNumber} resumed"), ("PaymentRun", run.Id));
        await db.SaveChangesAsync(ct);
        await executor.ExecuteAsync(run.Id, ct);
        return await mediator.Send(new GetPaymentRunByIdQuery(run.Id), ct);
    }
}

public record CancelPaymentRunCommand(int Id) : IRequest<PaymentRunDetailResponseModel>;

/// <summary>
/// Cancels an interrupted payment run: the vendors already paid stay paid and the rest are left unpaid
/// (<c>payment-run-not-resumable</c> unless the run is Interrupted or Pending).
/// </summary>
public class CancelPaymentRunHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<CancelPaymentRunCommand, PaymentRunDetailResponseModel>
{
    public async Task<PaymentRunDetailResponseModel> Handle(CancelPaymentRunCommand request, CancellationToken ct)
    {
        var run = await db.PaymentRuns.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Payment run {request.Id} not found");
        if (run.Status is not (PaymentRunStatus.Interrupted or PaymentRunStatus.Pending))
            throw VendorCreditMemoRules.Failure("Id", ResumePaymentRunHandler.NotResumableCode,
                $"Payment run {run.RunNumber} is {run.Status}; only an interrupted run can be cancelled.");

        run.Status = PaymentRunStatus.Cancelled;
        db.LogActivityAt("payment-run-cancelled", $"Payment run {run.RunNumber} cancelled; unpaid bills were left open", ("PaymentRun", run.Id));
        await db.SaveChangesAsync(ct);
        return await mediator.Send(new GetPaymentRunByIdQuery(run.Id), ct);
    }
}
