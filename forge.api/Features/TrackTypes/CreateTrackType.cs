using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TrackTypes;

public record CreateTrackTypeCommand(
    string Name,
    string Code,
    string? Description,
    List<StageRequestModel> Stages) : IRequest<TrackTypeResponseModel>;

public class CreateTrackTypeCommandValidator : AbstractValidator<CreateTrackTypeCommand>
{
    public CreateTrackTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Stages).NotEmpty().WithMessage("At least one stage is required.");
    }
}

public class CreateTrackTypeHandler(
    ITrackTypeRepository repo) : IRequestHandler<CreateTrackTypeCommand, TrackTypeResponseModel>
{
    public async Task<TrackTypeResponseModel> Handle(CreateTrackTypeCommand request, CancellationToken ct)
    {
        if (await repo.CodeExistsAsync(request.Code, null, ct))
            throw new InvalidOperationException($"Track type with code '{request.Code}' already exists.");

        var maxSortOrder = await repo.GetMaxSortOrderAsync(ct);

        var trackType = new TrackType
        {
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            IsDefault = false,
            SortOrder = maxSortOrder + 1,
            Stages = request.Stages.Select(s => new JobStage
            {
                Name = s.Name,
                Code = s.Code,
                SortOrder = s.SortOrder,
                Color = s.Color,
                WIPLimit = s.WIPLimit,
                IsIrreversible = false,
            }).ToList(),
        };

        await repo.AddAsync(trackType, ct);
        await repo.SaveChangesAsync(ct);

        return (await repo.GetByIdAsync(trackType.Id, ct))!;
    }
}
