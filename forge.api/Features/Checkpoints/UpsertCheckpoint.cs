using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Checkpoints;

/// <summary>
/// Upsert-by-WorldId. The blob is opaque — no shape validation beyond
/// non-empty, since forge-api is a storage surface for it, not a consumer.
/// See inventory.md B70.
/// </summary>
public record UpsertCheckpointCommand(string WorldId, string Blob) : IRequest;

public class UpsertCheckpointValidator : AbstractValidator<UpsertCheckpointCommand>
{
    public UpsertCheckpointValidator()
    {
        RuleFor(x => x.WorldId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Blob).NotEmpty();
    }
}

public class UpsertCheckpointHandler(AppDbContext db) : IRequestHandler<UpsertCheckpointCommand>
{
    public async Task Handle(UpsertCheckpointCommand request, CancellationToken ct)
    {
        var existing = await db.Checkpoints.FirstOrDefaultAsync(c => c.WorldId == request.WorldId, ct);
        if (existing is null)
        {
            db.Checkpoints.Add(new Checkpoint { WorldId = request.WorldId, Blob = request.Blob });
        }
        else
        {
            existing.Blob = request.Blob;
        }

        await db.SaveChangesAsync(ct);
    }
}
