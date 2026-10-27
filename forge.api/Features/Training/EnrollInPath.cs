using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Training;

public record EnrollInPathCommand(int PathId, EnrollInPathRequestModel Request, int AssignedByUserId) : IRequest<EnrollInPathResponseModel>;

public class EnrollInPathValidator : AbstractValidator<EnrollInPathCommand>
{
    public EnrollInPathValidator()
    {
        RuleFor(x => x.Request).Must(r => (r.UserIds?.Count ?? 0) + (r.Roles?.Count ?? 0) > 0)
            .WithMessage("Choose at least one person or role.");
    }
}

/// <summary>
/// Enrols the chosen users plus every active user holding one of the chosen roles. Someone
/// already enrolled keeps their enrolment (the due date is not changed); an enrolment that was
/// removed earlier is restored with the new due date and its progress intact.
/// </summary>
public class EnrollInPathHandler(AppDbContext db) : IRequestHandler<EnrollInPathCommand, EnrollInPathResponseModel>
{
    public async Task<EnrollInPathResponseModel> Handle(EnrollInPathCommand command, CancellationToken ct)
    {
        var path = await db.TrainingPaths.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.PathId, ct)
            ?? throw new KeyNotFoundException($"Training path {command.PathId} not found.");

        var requestedIds = command.Request.UserIds?.Distinct().ToList() ?? [];
        var roles = command.Request.Roles?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList() ?? [];
        var roleIds = db.Roles.Where(r => roles.Contains(r.Name!)).Select(r => r.Id);

        var userIds = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .WithoutServiceAccounts(db)
            .Where(u => requestedIds.Contains(u.Id) || db.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)))
            .Select(u => u.Id)
            .ToListAsync(ct);

        var existing = await db.TrainingPathEnrollments.IgnoreQueryFilters()
            .Where(e => e.PathId == path.Id && userIds.Contains(e.UserId))
            .ToListAsync(ct);
        var byUser = existing.ToDictionary(e => e.UserId);

        var enrolled = 0;
        var already = 0;
        foreach (var userId in userIds)
        {
            if (byUser.TryGetValue(userId, out var enrollment))
            {
                if (enrollment.DeletedAt is null)
                {
                    already++;
                    continue;
                }

                enrollment.DeletedAt = null;
                enrollment.DeletedBy = null;
                enrollment.DueDate = command.Request.DueDate;
                enrollment.AssignedByUserId = command.AssignedByUserId;
                enrollment.IsAutoAssigned = false;
            }
            else
            {
                db.TrainingPathEnrollments.Add(new TrainingPathEnrollment
                {
                    UserId = userId,
                    PathId = path.Id,
                    IsAutoAssigned = false,
                    AssignedByUserId = command.AssignedByUserId,
                    DueDate = command.Request.DueDate,
                });
            }

            enrolled++;
        }

        if (enrolled > 0)
            db.LogActivityAt(
                "enrolled",
                $"{enrolled} enrolled{(command.Request.DueDate is DateOnly due ? $", due {due:yyyy-MM-dd}" : string.Empty)}",
                ("TrainingPath", path.Id));
        await db.SaveChangesAsync(ct);

        return new EnrollInPathResponseModel(enrolled, already);
    }
}
