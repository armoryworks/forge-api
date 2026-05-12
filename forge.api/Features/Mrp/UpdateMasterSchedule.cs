using FluentValidation;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record UpdateMasterScheduleCommand(
    int Id,
    string Name,
    string? Description,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    List<UpdateMasterScheduleLineModel> Lines
) : IRequest<MasterScheduleDetailResponseModel>;

public record UpdateMasterScheduleLineModel(
    int? Id,
    int PartId,
    decimal Quantity,
    DateTimeOffset DueDate,
    string? Notes
);

public class UpdateMasterScheduleValidator : AbstractValidator<UpdateMasterScheduleCommand>
{
    public UpdateMasterScheduleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.PeriodEnd).GreaterThan(x => x.PeriodStart).WithMessage("Period end must be after period start.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.PartId).GreaterThan(0);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}

public class UpdateMasterScheduleHandler(AppDbContext db)
    : IRequestHandler<UpdateMasterScheduleCommand, MasterScheduleDetailResponseModel>
{
    public async Task<MasterScheduleDetailResponseModel> Handle(UpdateMasterScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await db.MasterSchedules
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Master schedule {request.Id} not found.");

        if (schedule.Status != MasterScheduleStatus.Draft)
            throw new InvalidOperationException("Only draft schedules can be edited.");

        schedule.Name = request.Name;
        schedule.Description = request.Description;
        schedule.PeriodStart = request.PeriodStart;
        schedule.PeriodEnd = request.PeriodEnd;

        // Sync lines: update existing, add new, remove missing
        var existingLineIds = schedule.Lines.Select(l => l.Id).ToHashSet();
        var incomingLineIds = request.Lines.Where(l => l.Id.HasValue).Select(l => l.Id!.Value).ToHashSet();

        // Remove lines not in incoming
        var toRemove = schedule.Lines.Where(l => !incomingLineIds.Contains(l.Id)).ToList();
        foreach (var line in toRemove)
            db.MasterScheduleLines.Remove(line);

        foreach (var incoming in request.Lines)
        {
            if (incoming.Id.HasValue && existingLineIds.Contains(incoming.Id.Value))
            {
                var existing = schedule.Lines.First(l => l.Id == incoming.Id.Value);
                existing.PartId = incoming.PartId;
                existing.Quantity = incoming.Quantity;
                existing.DueDate = incoming.DueDate;
                existing.Notes = incoming.Notes;
            }
            else
            {
                schedule.Lines.Add(new MasterScheduleLine
                {
                    PartId = incoming.PartId,
                    Quantity = incoming.Quantity,
                    DueDate = incoming.DueDate,
                    Notes = incoming.Notes,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Reload with Part nav
        await db.Entry(schedule).Collection(s => s.Lines).Query()
            .Include(l => l.Part)
            .LoadAsync(cancellationToken);

        return new MasterScheduleDetailResponseModel(
            schedule.Id,
            schedule.Name,
            schedule.Description,
            schedule.Status,
            schedule.PeriodStart,
            schedule.PeriodEnd,
            schedule.CreatedByUserId,
            schedule.CreatedAt,
            schedule.Lines.Select(l => new MasterScheduleLineResponseModel(
                l.Id,
                l.MasterScheduleId,
                l.PartId,
                l.Part?.PartNumber ?? "",
                l.Part?.Description,
                l.Quantity,
                l.DueDate,
                l.Notes
            )).ToList()
        );
    }
}
