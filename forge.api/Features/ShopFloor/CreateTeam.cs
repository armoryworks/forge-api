using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record CreateTeamCommand(string Name, string? Color, string? Description) : IRequest<TeamModel>;

public class CreateTeamValidator : AbstractValidator<CreateTeamCommand>
{
    public CreateTeamValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).MaximumLength(20);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class CreateTeamHandler(AppDbContext db) : IRequestHandler<CreateTeamCommand, TeamModel>
{
    public async Task<TeamModel> Handle(CreateTeamCommand request, CancellationToken ct)
    {
        var team = new Team
        {
            Name = request.Name,
            Color = request.Color,
            Description = request.Description,
        };

        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);

        return new TeamModel(team.Id, team.Name, team.Color, team.Description, 0);
    }
}
