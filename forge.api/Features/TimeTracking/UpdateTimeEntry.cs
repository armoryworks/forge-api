using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record UpdateTimeEntryCommand(int Id, UpdateTimeEntryRequestModel Data) : IRequest<TimeEntryResponseModel>;

public class UpdateTimeEntryCommandValidator : AbstractValidator<UpdateTimeEntryCommand>
{
    public UpdateTimeEntryCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.DurationMinutes).InclusiveBetween(1, 1440).When(x => x.Data.DurationMinutes.HasValue);
        RuleFor(x => x.Data.Category).MaximumLength(100).When(x => x.Data.Category is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(1000).When(x => x.Data.Notes is not null);
    }
}

public class UpdateTimeEntryHandler(ITimeTrackingRepository repo) : IRequestHandler<UpdateTimeEntryCommand, TimeEntryResponseModel>
{
    public async Task<TimeEntryResponseModel> Handle(UpdateTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindTimeEntryAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Time entry {request.Id} not found.");

        if (entry.IsLocked)
            throw new InvalidOperationException("This time entry is locked and cannot be edited.");

        if (entry.Date < DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime))
            throw new InvalidOperationException("Time entries from previous days cannot be edited.");

        var data = request.Data;
        if (data.JobId.HasValue) entry.JobId = data.JobId;
        if (data.Date.HasValue) entry.Date = data.Date.Value;
        if (data.DurationMinutes.HasValue) entry.DurationMinutes = data.DurationMinutes.Value;
        if (data.Category is not null) entry.Category = data.Category.Trim();
        if (data.Notes is not null) entry.Notes = data.Notes.Trim();

        await repo.SaveChangesAsync(cancellationToken);

        return (await repo.GetTimeEntryByIdAsync(entry.Id, cancellationToken))!;
    }
}
