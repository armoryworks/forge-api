using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

// F-14-BE-01: training paths were GET/seed-only. This adds the admin create path.
public record CreateTrainingPathCommand(CreateTrainingPathRequestModel Data) : IRequest<TrainingPathResponseModel>;

public class CreateTrainingPathValidator : AbstractValidator<CreateTrainingPathCommand>
{
    public CreateTrainingPathValidator()
    {
        RuleFor(x => x.Data.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Slug).NotEmpty().MaximumLength(200);
    }
}

public class CreateTrainingPathHandler(AppDbContext db) : IRequestHandler<CreateTrainingPathCommand, TrainingPathResponseModel>
{
    public async Task<TrainingPathResponseModel> Handle(CreateTrainingPathCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;

        var path = new TrainingPath
        {
            Title = data.Title.Trim(),
            Slug = data.Slug.Trim(),
            Description = data.Description?.Trim() ?? string.Empty,
            Icon = string.IsNullOrWhiteSpace(data.Icon) ? "school" : data.Icon!.Trim(),
            IsAutoAssigned = data.IsAutoAssigned,
            IsActive = data.IsActive,
            SortOrder = data.SortOrder,
            AllowedRoles = data.AllowedRoles,
        };
        db.TrainingPaths.Add(path);
        await db.SaveChangesAsync(cancellationToken);

        var moduleIds = data.ModuleIds ?? [];
        for (var i = 0; i < moduleIds.Count; i++)
            db.TrainingPathModules.Add(new TrainingPathModule { PathId = path.Id, ModuleId = moduleIds[i], Position = i, IsRequired = true });
        if (moduleIds.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return await TrainingPathResponseBuilder.BuildAsync(db, path, moduleIds, cancellationToken);
    }
}
