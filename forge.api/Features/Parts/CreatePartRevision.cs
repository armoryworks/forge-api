using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record CreatePartRevisionCommand(
    int PartId,
    string Revision,
    string? ChangeDescription,
    string? ChangeReason,
    DateTimeOffset EffectiveDate) : IRequest<PartRevisionResponseModel>;

public class CreatePartRevisionCommandValidator : AbstractValidator<CreatePartRevisionCommand>
{
    public CreatePartRevisionCommandValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ChangeDescription).MaximumLength(500).When(x => x.ChangeDescription is not null);
        RuleFor(x => x.ChangeReason).MaximumLength(500).When(x => x.ChangeReason is not null);
    }
}

public class CreatePartRevisionHandler(AppDbContext db, IPartRepository partRepo)
    : IRequestHandler<CreatePartRevisionCommand, PartRevisionResponseModel>
{
    public async Task<PartRevisionResponseModel> Handle(CreatePartRevisionCommand request, CancellationToken cancellationToken)
    {
        var part = await partRepo.FindAsync(request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var exists = await db.PartRevisions
            .AnyAsync(r => r.PartId == request.PartId && r.Revision == request.Revision.Trim(), cancellationToken);
        if (exists)
            throw new InvalidOperationException($"Revision '{request.Revision}' already exists for this part");

        // Clear current flag on all existing revisions
        var existingRevisions = await db.PartRevisions
            .Where(r => r.PartId == request.PartId && r.IsCurrent)
            .ToListAsync(cancellationToken);
        foreach (var rev in existingRevisions)
            rev.IsCurrent = false;

        var revision = new PartRevision
        {
            PartId = request.PartId,
            Revision = request.Revision.Trim(),
            ChangeDescription = request.ChangeDescription?.Trim(),
            ChangeReason = request.ChangeReason?.Trim(),
            EffectiveDate = request.EffectiveDate,
            IsCurrent = true,
        };

        db.PartRevisions.Add(revision);

        // Update the part's current revision
        part.Revision = request.Revision.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return new PartRevisionResponseModel(
            revision.Id,
            revision.PartId,
            revision.Revision,
            revision.ChangeDescription,
            revision.ChangeReason,
            revision.EffectiveDate,
            revision.IsCurrent,
            0,
            revision.CreatedAt);
    }
}
