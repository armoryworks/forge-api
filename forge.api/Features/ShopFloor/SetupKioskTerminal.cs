using FluentValidation;
using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record KioskTerminalModel(int Id, string Name, string DeviceToken, int TeamId, string TeamName, string? TeamColor);

public record SetupKioskTerminalCommand(string Name, string DeviceToken, int TeamId, int ConfiguredByUserId) : IRequest<KioskTerminalModel>;

public class SetupKioskTerminalValidator : AbstractValidator<SetupKioskTerminalCommand>
{
    public SetupKioskTerminalValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceToken).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.ConfiguredByUserId).GreaterThan(0);
    }
}

public class SetupKioskTerminalHandler(AppDbContext db) : IRequestHandler<SetupKioskTerminalCommand, KioskTerminalModel>
{
    public async Task<KioskTerminalModel> Handle(SetupKioskTerminalCommand request, CancellationToken ct)
    {
        var team = await db.Teams.FindAsync([request.TeamId], ct)
            ?? throw new KeyNotFoundException($"Team {request.TeamId} not found");

        // Upsert by device token
        var terminal = await db.KioskTerminals
            .FirstOrDefaultAsync(t => t.DeviceToken == request.DeviceToken, ct);

        if (terminal == null)
        {
            terminal = new KioskTerminal
            {
                Name = request.Name,
                DeviceToken = request.DeviceToken,
                TeamId = request.TeamId,
                ConfiguredByUserId = request.ConfiguredByUserId,
            };
            db.KioskTerminals.Add(terminal);
        }
        else
        {
            terminal.Name = request.Name;
            terminal.TeamId = request.TeamId;
            terminal.ConfiguredByUserId = request.ConfiguredByUserId;
            terminal.IsActive = true;
        }

        await db.SaveChangesAsync(ct);

        return new KioskTerminalModel(terminal.Id, terminal.Name, terminal.DeviceToken, team.Id, team.Name, team.Color);
    }
}
