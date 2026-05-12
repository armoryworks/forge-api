using FluentValidation;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record CreateMasterScheduleCommand(
    string Name,
    string? Description,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int CreatedByUserId,
    List<CreateMasterScheduleLineModel> Lines
) : IRequest<MasterScheduleDetailResponseModel>;

public record CreateMasterScheduleLineModel(
    int PartId,
    decimal Quantity,
    DateTimeOffset DueDate,
    string? Notes
);

public class CreateMasterScheduleValidator : AbstractValidator<CreateMasterScheduleCommand>
{
    public CreateMasterScheduleValidator()
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

public class CreateMasterScheduleHandler(AppDbContext db)
    : IRequestHandler<CreateMasterScheduleCommand, MasterScheduleDetailResponseModel>
{
    public async Task<MasterScheduleDetailResponseModel> Handle(CreateMasterScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = new MasterSchedule
        {
            Name = request.Name,
            Description = request.Description,
            Status = MasterScheduleStatus.Draft,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            CreatedByUserId = request.CreatedByUserId,
            Lines = request.Lines.Select(l => new MasterScheduleLine
            {
                PartId = l.PartId,
                Quantity = l.Quantity,
                DueDate = l.DueDate,
                Notes = l.Notes,
            }).ToList(),
        };

        db.MasterSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);

        // Reload with Part nav for response
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
