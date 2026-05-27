using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

// F-14-BE-01: admin edit of a training path. A non-null ModuleIds replaces the path's
// module set (in order); null leaves the modules untouched.
public record UpdateTrainingPathCommand(int Id, UpdateTrainingPathRequestModel Data) : IRequest<TrainingPathResponseModel>;

public class UpdateTrainingPathValidator : AbstractValidator<UpdateTrainingPathCommand>
{
    public UpdateTrainingPathValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Slug).NotEmpty().MaximumLength(200);
    }
}

public class UpdateTrainingPathHandler(AppDbContext db) : IRequestHandler<UpdateTrainingPathCommand, TrainingPathResponseModel>
{
    public async Task<TrainingPathResponseModel> Handle(UpdateTrainingPathCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;

        var path = await db.TrainingPaths
            .Include(p => p.PathModules)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Training path {request.Id} not found");

        path.Title = data.Title.Trim();
        path.Slug = data.Slug.Trim();
        path.Description = data.Description?.Trim() ?? string.Empty;
        path.Icon = string.IsNullOrWhiteSpace(data.Icon) ? "school" : data.Icon!.Trim();
        path.IsAutoAssigned = data.IsAutoAssigned;
        path.IsActive = data.IsActive;
        path.SortOrder = data.SortOrder;
        path.AllowedRoles = data.AllowedRoles;

        var effectiveModuleIds = data.ModuleIds
            ?? path.PathModules.OrderBy(pm => pm.Position).Select(pm => pm.ModuleId).ToList();

        if (data.ModuleIds is not null)
        {
            db.TrainingPathModules.RemoveRange(path.PathModules);
            for (var i = 0; i < data.ModuleIds.Count; i++)
                db.TrainingPathModules.Add(new TrainingPathModule { PathId = path.Id, ModuleId = data.ModuleIds[i], Position = i, IsRequired = true });
        }

        await db.SaveChangesAsync(cancellationToken);

        return await TrainingPathResponseBuilder.BuildAsync(db, path, effectiveModuleIds, cancellationToken);
    }
}
