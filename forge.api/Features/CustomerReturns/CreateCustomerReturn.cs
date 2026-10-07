using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
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
    bool CreateReworkJob) : IRequest<CustomerReturnListItemModel>;

public class CreateCustomerReturnValidator : AbstractValidator<CreateCustomerReturnCommand>
{
    public CreateCustomerReturnValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.OriginalJobId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateCustomerReturnHandler(
    AppDbContext db,
    IMediator mediator,
    IHttpContextAccessor httpContext,
    IJobRepository jobRepo,
    IBusinessIdentifierService identifiers)
    : IRequestHandler<CreateCustomerReturnCommand, CustomerReturnListItemModel>
{
    public async Task<CustomerReturnListItemModel> Handle(CreateCustomerReturnCommand request, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([request.CustomerId], ct)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var originalJob = await db.Jobs
            .Include(j => j.TrackType)
            .ThenInclude(t => t.Stages)
            .FirstOrDefaultAsync(j => j.Id == request.OriginalJobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.OriginalJobId} not found");

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
            Status = CustomerReturnStatus.Received,
        };

        Job? reworkJob = null;
        if (request.CreateReworkJob)
        {
            // Get first stage of the same track type for the rework job
            var firstStage = originalJob.TrackType.Stages
                .OrderBy(s => s.SortOrder)
                .First();

            var reworkJobNumber = await jobRepo.GenerateNextJobNumberAsync(ct);
            var maxPosition = await jobRepo.GetMaxBoardPositionAsync(firstStage.Id, ct);

            reworkJob = new Job
            {
                JobNumber = reworkJobNumber,
                Title = $"[Rework] {originalJob.Title}",
                Description = $"Rework for RMA {returnNumber}. Reason: {request.Reason}",
                TrackTypeId = originalJob.TrackTypeId,
                CurrentStageId = firstStage.Id,
                BoardPosition = maxPosition + 1,
                CustomerId = request.CustomerId,
                Priority = JobPriority.High,
                AssigneeId = originalJob.AssigneeId,
            };
            reworkJob.ActivityLogs.Add(new JobActivityLog
            {
                Action = ActivityAction.Created,
                Description = $"Rework job {reworkJobNumber} created for RMA {returnNumber}.",
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
}
