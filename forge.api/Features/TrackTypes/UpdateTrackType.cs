using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.TrackTypes;

public record UpdateTrackTypeCommand(
    int Id,
    string Name,
    string Code,
    string? Description,
    List<StageRequestModel> Stages) : IRequest<TrackTypeResponseModel>;

public class UpdateTrackTypeValidator : AbstractValidator<UpdateTrackTypeCommand>
{
    public UpdateTrackTypeValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.Stages).NotEmpty().WithMessage("At least one stage is required");
        RuleForEach(x => x.Stages).ChildRules(stage =>
        {
            stage.RuleFor(s => s.Name).NotEmpty().MaximumLength(100);
            stage.RuleFor(s => s.Code).NotEmpty().MaximumLength(50);
            stage.RuleFor(s => s.SortOrder).GreaterThanOrEqualTo(0);
            stage.RuleFor(s => s.Color).NotEmpty().MaximumLength(20);
            stage.RuleFor(s => s.WIPLimit).GreaterThan(0).When(s => s.WIPLimit.HasValue);
        });
    }
}

public class UpdateTrackTypeHandler(
    ITrackTypeRepository repo,
    AppDbContext db) : IRequestHandler<UpdateTrackTypeCommand, TrackTypeResponseModel>
{
    public async Task<TrackTypeResponseModel> Handle(UpdateTrackTypeCommand request, CancellationToken ct)
    {
        var trackType = await repo.FindAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Track type with ID {request.Id} not found.");

        if (await repo.CodeExistsAsync(request.Code, request.Id, ct))
            throw new InvalidOperationException($"Track type with code '{request.Code}' already exists.");

        var hidden = new List<JobStage>();
        var shown = new List<JobStage>();
        foreach (var existing in trackType.Stages)
        {
            var visible = FindRequested(request, existing.Code)?.IsActive ?? false;
            if (existing.IsActive && !visible)
                hidden.Add(existing);
            else if (!existing.IsActive && visible)
                shown.Add(existing);
        }

        var anyVisible = trackType.Stages.Any(s => FindRequested(request, s.Code)?.IsActive ?? false)
            || request.Stages.Any(r => r.IsActive && trackType.Stages.All(s => s.Code != r.Code));
        if (!anyVisible)
            throw new InvalidOperationException("At least one status must stay visible.");

        await EnsureCanHideAsync(hidden, ct);

        trackType.Name = request.Name;
        trackType.Code = request.Code;
        trackType.Description = request.Description;

        foreach (var existing in trackType.Stages.Where(s => FindRequested(request, s.Code) is null))
            existing.IsActive = false;

        foreach (var stageReq in request.Stages)
        {
            var existing = trackType.Stages.FirstOrDefault(s => s.Code == stageReq.Code);
            if (existing != null)
            {
                existing.Name = stageReq.Name;
                existing.SortOrder = stageReq.SortOrder;
                existing.Color = stageReq.Color;
                existing.WIPLimit = stageReq.WIPLimit;
                existing.IsIrreversible = stageReq.IsIrreversible;
                existing.IsActive = stageReq.IsActive;
            }
            else
            {
                trackType.Stages.Add(new JobStage
                {
                    Name = stageReq.Name,
                    Code = stageReq.Code,
                    SortOrder = stageReq.SortOrder,
                    Color = stageReq.Color,
                    WIPLimit = stageReq.WIPLimit,
                    IsIrreversible = stageReq.IsIrreversible,
                    IsActive = stageReq.IsActive,
                });
            }
        }

        var (action, description) = DescribeChange(trackType.Name, hidden, shown);
        db.LogActivityAt(action, description, ("TrackType", trackType.Id));

        await repo.SaveChangesAsync(ct);
        return (await repo.GetByIdAsync(trackType.Id, ct))!;
    }

    private static StageRequestModel? FindRequested(UpdateTrackTypeCommand request, string code) =>
        request.Stages.FirstOrDefault(s => s.Code == code);

    private async Task EnsureCanHideAsync(List<JobStage> hidden, CancellationToken ct)
    {
        foreach (var stage in hidden)
        {
            if (stage.IsMandatory)
                throw new InvalidOperationException($"{stage.Name} is a required status and can't be hidden.");

            var openJobs = await db.Jobs.CountAsync(j => j.CurrentStageId == stage.Id && !j.IsArchived, ct);
            if (openJobs > 0)
                throw new InvalidOperationException(openJobs == 1
                    ? $"Move the 1 work order in {stage.Name} to another status first."
                    : $"Move the {openJobs} work orders in {stage.Name} to another status first.");
        }
    }

    private static (string Action, string Description) DescribeChange(
        string trackTypeName, List<JobStage> hidden, List<JobStage> shown)
    {
        if (hidden.Count == 0 && shown.Count == 0)
            return ("updated", $"Updated order type {trackTypeName}.");

        var parts = new List<string>();
        if (hidden.Count > 0)
            parts.Add($"Hid {string.Join(", ", hidden.OrderBy(s => s.SortOrder).Select(s => s.Name))}");
        if (shown.Count > 0)
            parts.Add($"Showed {string.Join(", ", shown.OrderBy(s => s.SortOrder).Select(s => s.Name))}");
        return ("stage-visibility-changed", $"{string.Join("; ", parts)}.");
    }
}
