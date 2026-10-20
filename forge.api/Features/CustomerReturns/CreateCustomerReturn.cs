using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.Terminology;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerReturns;

public record CreateCustomerReturnCommand(
    int CustomerId,
    int OriginalJobId,
    string Reason,
    string? Notes,
    DateTimeOffset ReturnDate,
    bool CreateReworkJob,
    decimal? Quantity = null) : IRequest<CustomerReturnListItemModel>;

public class CreateCustomerReturnValidator : AbstractValidator<CreateCustomerReturnCommand>
{
    public CreateCustomerReturnValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.OriginalJobId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Quantity).GreaterThan(0m).When(x => x.Quantity.HasValue);
    }
}

public class CreateCustomerReturnHandler(
    AppDbContext db,
    IMediator mediator,
    IHttpContextAccessor httpContext,
    IJobRepository jobRepo,
    IBusinessIdentifierService identifiers,
    ITerminologyLabels? terminology = null)
    : IRequestHandler<CreateCustomerReturnCommand, CustomerReturnListItemModel>
{
    public async Task<CustomerReturnListItemModel> Handle(CreateCustomerReturnCommand request, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([request.CustomerId], ct)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var originalJob = await db.Jobs
            .Include(j => j.TrackType)
            .ThenInclude(t => t.Stages)
            .Include(j => j.JobParts)
            .Include(j => j.SalesOrderLine)
            .ThenInclude(l => l!.SalesOrder)
            .FirstOrDefaultAsync(j => j.Id == request.OriginalJobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.OriginalJobId} not found");

        var jobCustomerId = originalJob.CustomerId ?? originalJob.SalesOrderLine?.SalesOrder?.CustomerId;
        if (jobCustomerId is int ownerId && ownerId != request.CustomerId)
            throw Rejected("originalJobId", request.OriginalJobId,
                $"Job {originalJob.JobNumber} belongs to a different customer.");

        if (request.Quantity is decimal quantity && originalJob.SalesOrderLine is { } soLine)
        {
            var alreadyReturned = await db.CustomerReturns
                .Where(r => r.OriginalJob!.SalesOrderLineId == soLine.Id && r.Quantity != null)
                .SumAsync(r => r.Quantity!.Value, ct);
            var returnable = soLine.ShippedQuantity - alreadyReturned;
            if (quantity > returnable)
                throw Rejected("quantity", quantity,
                    $"Only {Math.Max(0m, returnable):0.####} can be returned: {soLine.ShippedQuantity:0.####} shipped, {alreadyReturned:0.####} already returned.");
        }

        // Generate next return number
        var lastNumber = await db.CustomerReturns
            .OrderByDescending(r => r.Id)
            .Select(r => r.ReturnNumber)
            .FirstOrDefaultAsync(ct);
        var nextSeq = 1;
        if (lastNumber != null && lastNumber.StartsWith("RMA-") && int.TryParse(lastNumber[4..], out var seq))
            nextSeq = seq + 1;
        var returnNumber = $"RMA-{nextSeq:D5}";

        var customerReturn = new CustomerReturn
        {
            ReturnNumber = returnNumber,
            CustomerId = request.CustomerId,
            OriginalJobId = request.OriginalJobId,
            Reason = request.Reason,
            Notes = request.Notes,
            ReturnDate = request.ReturnDate,
            Quantity = request.Quantity,
            Status = CustomerReturnStatus.Received,
        };

        Job? reworkJob = null;
        if (request.CreateReworkJob)
        {
            // Get first stage of the same track type for the rework job
            var firstStage = originalJob.TrackType.Stages
                .Where(s => s.IsActive)
                .OrderBy(s => s.SortOrder)
                .First();

            var reworkJobNumber = await jobRepo.GenerateNextJobNumberAsync(ct);
            var maxPosition = await jobRepo.GetMaxBoardPositionAsync(firstStage.Id, ct);

            reworkJob = new Job
            {
                JobNumber = reworkJobNumber,
                Title = $"[Rework] {originalJob.Title}",
                Description = request.Quantity is decimal returnedQuantity
                    ? $"Rework for RMA {returnNumber}. Reason: {request.Reason}. Quantity returned: {returnedQuantity:0.####}"
                    : $"Rework for RMA {returnNumber}. Reason: {request.Reason}",
                TrackTypeId = originalJob.TrackTypeId,
                CurrentStageId = firstStage.Id,
                BoardPosition = maxPosition + 1,
                CustomerId = request.CustomerId,
                Priority = JobPriority.High,
                AssigneeId = originalJob.AssigneeId,
                PartId = originalJob.PartId,
                PartRevision = originalJob.PartRevision,
            };
            if (originalJob.PartId is int reworkPartId)
            {
                reworkJob.JobParts.Add(new JobPart
                {
                    PartId = reworkPartId,
                    Quantity = request.Quantity ?? OperationTimeMath.JobBuildQuantity(originalJob),
                });
            }
            var jobTerm = TermLabels.Lower((await terminology.GetOrDefaultsAsync(ct)).Singular("job"));
            reworkJob.ActivityLogs.Add(new JobActivityLog
            {
                Action = ActivityAction.Created,
                Description = $"Rework {jobTerm} {reworkJobNumber} created for RMA {returnNumber}.",
            });
            db.Jobs.Add(reworkJob);
            await db.SaveChangesAsync(ct);

            await identifiers.IssueAsync(BusinessEntityType.Job, reworkJob.Id, reworkJob.JobNumber, ct);

            customerReturn.ReworkJobId = reworkJob.Id;
            customerReturn.Status = CustomerReturnStatus.ReworkOrdered;

            // Create a job link between original and rework
            db.JobLinks.Add(new JobLink
            {
                SourceJobId = request.OriginalJobId,
                TargetJobId = reworkJob.Id,
                LinkType = JobLinkType.RelatedTo,
            });
        }

        db.CustomerReturns.Add(customerReturn);
        await db.SaveChangesAsync(ct);

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await mediator.Publish(new CustomerReturnReceivedEvent(customerReturn.Id, request.OriginalJobId, userId), ct);

        return new CustomerReturnListItemModel(
            customerReturn.Id, customerReturn.ReturnNumber,
            customerReturn.CustomerId, customer.Name,
            customerReturn.OriginalJobId, originalJob.JobNumber,
            reworkJob?.Id, reworkJob?.JobNumber,
            customerReturn.Status.ToString(), customerReturn.Reason,
            customerReturn.ReturnDate, customerReturn.CreatedAt);
    }

    private static ValidationException Rejected(string fieldPath, object attemptedValue, string message) =>
        new(new[] { new ValidationFailure(fieldPath, message) { AttemptedValue = attemptedValue } });
}
