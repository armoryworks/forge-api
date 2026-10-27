using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Shipping;

public record ConfirmPickLineCommand(int WaveId, int LineId, ConfirmPickLineRequestModel Request) : IRequest;

public class ConfirmPickLineValidator : AbstractValidator<ConfirmPickLineCommand>
{
    public ConfirmPickLineValidator()
    {
        RuleFor(x => x.Request.PickedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.ShortNotes).MaximumLength(500);
    }
}

public class ConfirmPickLineHandler(AppDbContext db, IClock clock) : IRequestHandler<ConfirmPickLineCommand>
{
    public async Task Handle(ConfirmPickLineCommand command, CancellationToken cancellationToken)
    {
        var line = await db.PickLines
            .Include(l => l.Wave)
            .FirstOrDefaultAsync(l => l.Id == command.LineId && l.WaveId == command.WaveId, cancellationToken)
            ?? throw new KeyNotFoundException($"Pick line {command.LineId} not found in wave {command.WaveId}");

        var wave = line.Wave;
        if (wave.Status != PickWaveStatus.Released && wave.Status != PickWaveStatus.InProgress)
            throw new InvalidOperationException($"Release the wave before picking; wave {wave.WaveNumber} is {wave.Status}.");

        if (line.Status != PickLineStatus.Pending)
            throw new InvalidOperationException($"Pick line is already in status {line.Status}");

        if (command.Request.PickedQuantity > line.RequestedQuantity)
            throw new InvalidOperationException(
                $"Picked quantity {command.Request.PickedQuantity:0.####} is more than the {line.RequestedQuantity:0.####} requested.");

        line.PickedQuantity = command.Request.PickedQuantity;
        line.PickedAt = clock.UtcNow;
        line.PickedByUserId = db.CurrentUserId;
        line.ShortNotes = string.IsNullOrWhiteSpace(command.Request.ShortNotes) ? null : command.Request.ShortNotes.Trim();
        line.Status = command.Request.PickedQuantity >= line.RequestedQuantity
            ? PickLineStatus.Picked
            : PickLineStatus.Short;

        if (wave.Status == PickWaveStatus.Released)
        {
            wave.Status = PickWaveStatus.InProgress;
            wave.StartedAt = clock.UtcNow;
        }

        wave.PickedLines = await db.PickLines
            .CountAsync(l => l.WaveId == wave.Id && l.Id != line.Id
                && (l.Status == PickLineStatus.Picked || l.Status == PickLineStatus.Short), cancellationToken) + 1;

        db.LogActivityAt(line.Status == PickLineStatus.Short ? "pick-line-short" : "pick-line-picked",
            $"Line {line.SortOrder} picked {line.PickedQuantity:0.####} of {line.RequestedQuantity:0.####}",
            ("PickWave", wave.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
