using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record RestoreTrainingModuleCommand(int Id) : IRequest;

public class RestoreTrainingModuleHandler(AppDbContext db) : IRequestHandler<RestoreTrainingModuleCommand>
{
    public async Task Handle(RestoreTrainingModuleCommand request, CancellationToken ct)
    {
        var module = await db.TrainingModules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Training module {request.Id} not found.");

        if (module.DeletedAt is null) return;

        var slugTaken = await db.TrainingModules.AnyAsync(m => m.Slug == module.Slug && m.Id != module.Id, ct);
        if (slugTaken)
            throw new InvalidOperationException($"Another training module already uses the slug '{module.Slug}'.");

        module.DeletedAt = null;
        module.DeletedBy = null;
        await db.SaveChangesAsync(ct);
    }
}
