using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Admin;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Employees;

public record TerminateEmployeeCommand(
    int EmployeeId,
    string? Kind,
    TerminateEmployeeRequestModel Request,
    int CallerUserId,
    bool CallerIsAdmin) : IRequest<EmployeeTerminationResponseModel>;

public class TerminateEmployeeValidator : AbstractValidator<TerminateEmployeeCommand>
{
    public TerminateEmployeeValidator()
    {
        RuleFor(x => x.Request.TerminationDate).NotEmpty();
        RuleFor(x => x.Request.ReasonCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Notes).MaximumLength(2000);
    }
}

/// <summary>
/// Records a termination. An effective date of today or earlier (shop time zone) applies it at
/// once through <see cref="EmployeeTermination"/>; a later date is applied by
/// <see cref="Forge.Api.Jobs.ApplyScheduledTerminationsJob"/> on that day. Re-terminating a recorded but
/// not yet applied termination replaces its details.
/// </summary>
public class TerminateEmployeeHandler(AppDbContext db, EmployeeTermination termination, IClock clock)
    : IRequestHandler<TerminateEmployeeCommand, EmployeeTerminationResponseModel>
{
    public async Task<EmployeeTerminationResponseModel> Handle(TerminateEmployeeCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var profile = await EmployeeTarget.ResolveAsync(
            db, command.EmployeeId, command.Kind, command.CallerUserId, command.CallerIsAdmin, ct);

        if (profile.TerminationAppliedAt is not null)
            throw new InvalidOperationException("This employee is already terminated. Rehire them before recording a new termination.");

        var reason = request.ReasonCode.Trim();
        var reasonKnown = await db.ReferenceData.AnyAsync(
            r => r.GroupCode == TerminationReasons.GroupCode && r.Code == reason && r.IsActive, ct);
        if (!reasonKnown)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                "ReasonCode", $"'{reason}' is not a termination reason.")]);

        if (profile.UserId is int userId)
            await UserDeactivation.EnsureNotLastActiveAdminAsync(db, userId, ct);

        profile.TerminationDate = request.TerminationDate;
        profile.TerminationReasonCode = reason;
        profile.TerminationNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        profile.RehireEligible = request.RehireEligible;
        profile.FinalPayRequired = request.FinalPayRequired;
        profile.FinalPayProcessedAt = null;
        profile.TerminatedByUserId = command.CallerUserId == 0 ? null : command.CallerUserId;

        var zone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);

        TerminationEffectsModel? effects = null;
        if (request.TerminationDate <= today)
        {
            effects = await termination.ApplyAsync(profile, command.CallerUserId, ct);
        }
        else
        {
            db.LogActivityAt(
                "termination-scheduled",
                $"Termination scheduled for {request.TerminationDate:yyyy-MM-dd} ({reason})",
                ("Employee", profile.UserId ?? profile.Id));
            await db.SaveChangesAsync(ct);
        }

        return new EmployeeTerminationResponseModel(
            profile.Id,
            profile.UserId,
            request.TerminationDate,
            reason,
            profile.TerminationNotes,
            request.RehireEligible,
            request.FinalPayRequired,
            profile.TerminationAppliedAt,
            effects);
    }
}
