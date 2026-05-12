using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

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
    ITrackTypeRepository repo) : IRequestHandler<UpdateTrackTypeCommand, TrackTypeResponseModel>
{
    public async Task<TrackTypeResponseModel> Handle(UpdateTrackTypeCommand request, CancellationToken ct)
    {
        var trackType = await repo.FindAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Track type with ID {request.Id} not found.");

        if (await repo.CodeExistsAsync(request.Code, request.Id, ct))
            throw new InvalidOperationException($"Track type with code '{request.Code}' already exists.");

        trackType.Name = request.Name;
        trackType.Code = request.Code;
        trackType.Description = request.Description;

        // Mark removed stages as inactive
        var requestedCodes = request.Stages.Select(s => s.Code).ToHashSet();
        foreach (var existing in trackType.Stages)
        {
            if (!requestedCodes.Contains(existing.Code))
                existing.IsActive = false;
        }

        // Update existing stages and add new ones
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
                existing.IsActive = true;
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
                });
            }
        }

        await repo.SaveChangesAsync(ct);
        return (await repo.GetByIdAsync(trackType.Id, ct))!;
    }
}
