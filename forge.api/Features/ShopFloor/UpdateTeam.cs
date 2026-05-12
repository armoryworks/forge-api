using FluentValidation;
using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record UpdateTeamCommand(int Id, string Name, string? Color, string? Description, bool IsActive) : IRequest<TeamModel>;

public class UpdateTeamValidator : AbstractValidator<UpdateTeamCommand>
{
    public UpdateTeamValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).MaximumLength(20);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class UpdateTeamHandler(AppDbContext db) : IRequestHandler<UpdateTeamCommand, TeamModel>
{
    public async Task<TeamModel> Handle(UpdateTeamCommand request, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == request.Id && t.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Team {request.Id} not found");

        team.Name = request.Name;
        team.Color = request.Color;
        team.Description = request.Description;
        team.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        var memberCount = await db.Users.CountAsync(u => u.IsActive && u.TeamId == team.Id, ct);

        return new TeamModel(team.Id, team.Name, team.Color, team.Description, memberCount);
    }
}
