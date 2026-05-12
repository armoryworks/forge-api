using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ShiftAssignments;

public record CreateShiftAssignmentCommand(CreateShiftAssignmentRequestModel Request) : IRequest<ShiftAssignmentResponseModel>;

public class CreateShiftAssignmentValidator : AbstractValidator<CreateShiftAssignmentCommand>
{
    public CreateShiftAssignmentValidator()
    {
        RuleFor(x => x.Request.UserId).GreaterThan(0);
        RuleFor(x => x.Request.ShiftId).GreaterThan(0);
        RuleFor(x => x.Request.EffectiveFrom).NotEmpty();
        RuleFor(x => x.Request.ShiftDifferentialRate)
            .GreaterThanOrEqualTo(0).When(x => x.Request.ShiftDifferentialRate.HasValue);
    }
}

public class CreateShiftAssignmentHandler(AppDbContext db) : IRequestHandler<CreateShiftAssignmentCommand, ShiftAssignmentResponseModel>
{
    public async Task<ShiftAssignmentResponseModel> Handle(CreateShiftAssignmentCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FindAsync([request.Request.UserId], cancellationToken);
        // Phase 3 H2 / WU-12: employee-active check on shift assignment.
        // Deactivated users cannot be the target of new assignments.
        ActiveCheck.EnsureActive(user, "Employee", "userId", request.Request.UserId);

        var shift = await db.Shifts.FindAsync([request.Request.ShiftId], cancellationToken)
            ?? throw new KeyNotFoundException($"Shift {request.Request.ShiftId} not found");

        // Close any current open assignment for this user
        var currentAssignment = await db.ShiftAssignments
            .Where(sa => sa.UserId == request.Request.UserId && sa.EffectiveTo == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentAssignment != null)
        {
            currentAssignment.EffectiveTo = request.Request.EffectiveFrom.AddDays(-1);
        }

        var assignment = new ShiftAssignment
        {
            UserId = request.Request.UserId,
            ShiftId = request.Request.ShiftId,
            EffectiveFrom = request.Request.EffectiveFrom,
            EffectiveTo = request.Request.EffectiveTo,
            ShiftDifferentialRate = request.Request.ShiftDifferentialRate,
            Notes = request.Request.Notes?.Trim(),
        };

        db.ShiftAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        var userName = user.LastName + ", " + user.FirstName;

        return new ShiftAssignmentResponseModel(
            assignment.Id, assignment.UserId, userName,
            assignment.ShiftId, shift.Name,
            assignment.EffectiveFrom, assignment.EffectiveTo,
            assignment.ShiftDifferentialRate, assignment.Notes);
    }
}
