using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record CreateTimeEntryCommand(CreateTimeEntryRequestModel Data) : IRequest<TimeEntryResponseModel>;

public class CreateTimeEntryCommandValidator : AbstractValidator<CreateTimeEntryCommand>
{
    public CreateTimeEntryCommandValidator()
    {
        RuleFor(x => x.Data.DurationMinutes).InclusiveBetween(1, 1440).WithMessage("Duration must be between 1 and 1440 minutes.");
        RuleFor(x => x.Data.Category).MaximumLength(100).When(x => x.Data.Category is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(1000).When(x => x.Data.Notes is not null);
    }
}

public class CreateTimeEntryHandler(
    ITimeTrackingRepository repo, IHttpContextAccessor httpContext, AppDbContext db, IJobCostService? jobCost = null)
    : IRequestHandler<CreateTimeEntryCommand, TimeEntryResponseModel>
{
    public async Task<TimeEntryResponseModel> Handle(CreateTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await TimeLockBoundary.EnsureOpenAsync(db, data.Date, cancellationToken);

        var entry = new TimeEntry
        {
            UserId = userId,
            JobId = data.JobId,
            Date = data.Date,
            DurationMinutes = data.DurationMinutes,
            Category = data.Category?.Trim(),
            Notes = data.Notes?.Trim(),
            IsManual = true,
            OperationId = data.OperationId,
            EntryType = data.EntryType,
            IsBillable = data.IsBillable,
        };

        if (jobCost is not null && entry.JobId.HasValue)
            await jobCost.PriceTimeEntryAsync(entry, cancellationToken);

        await repo.AddTimeEntryAsync(entry, cancellationToken);

        return (await repo.GetTimeEntryByIdAsync(entry.Id, cancellationToken))!;
    }
}
