using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Admin;

public record SetMfaPolicyCommand(IReadOnlyList<string> RequiredRoles, int? EnrollmentGraceDays = null) : IRequest;

public class SetMfaPolicyValidator : AbstractValidator<SetMfaPolicyCommand>
{
    public const int MaxGraceDays = 90;

    public SetMfaPolicyValidator()
    {
        RuleFor(x => x.EnrollmentGraceDays)
            .InclusiveBetween(0, MaxGraceDays)
            .When(x => x.EnrollmentGraceDays.HasValue)
            .WithMessage($"Enrolment grace must be between 0 and {MaxGraceDays} days.");
    }
}

public class SetMfaPolicyHandler(AppDbContext db, UserManager<ApplicationUser> userManager) : IRequestHandler<SetMfaPolicyCommand>
{
    public async Task Handle(SetMfaPolicyCommand request, CancellationToken cancellationToken)
    {
        await MfaRolePolicy.SetRequiredRolesAsync(db, request.RequiredRoles, cancellationToken);
        if (request.EnrollmentGraceDays is { } graceDays)
            await MfaRolePolicy.SetGraceDaysAsync(db, graceDays, cancellationToken);

        var users = await db.Users.ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var enforced = MfaRolePolicy.AppliesTo(request.RequiredRoles, user, roles);
            user.MfaEnforcedByPolicy = enforced;
            if (!enforced)
                user.MfaEnrollmentGraceStartedAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
