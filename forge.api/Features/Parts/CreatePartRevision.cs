using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

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
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(10);
        RuleFor(x => x.ChangeDescription).MaximumLength(500).When(x => x.ChangeDescription is not null);
        RuleFor(x => x.ChangeReason).MaximumLength(500).When(x => x.ChangeReason is not null);
    }
}

public class CreatePartRevisionHandler(
    AppDbContext db,
    IPartRepository partRepo,
    IHttpContextAccessor? httpContextAccessor = null)
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

        var creator = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == uid, cancellationToken)
            : null;

        var revision = new PartRevision
        {
            PartId = request.PartId,
            Revision = request.Revision.Trim(),
            ChangeDescription = request.ChangeDescription?.Trim(),
            ChangeReason = request.ChangeReason?.Trim(),
            EffectiveDate = request.EffectiveDate,
            IsCurrent = true,
            CreatedBy = creator?.Id,
        };

        db.PartRevisions.Add(revision);

        var previousRevision = part.Revision;

        // Update the part's current revision
        part.Revision = request.Revision.Trim();

        var summary = string.IsNullOrWhiteSpace(previousRevision)
            ? $"Revised to {revision.Revision}"
            : $"Revised {previousRevision} to {revision.Revision}";
        db.LogActivityAt(
            "revised",
            string.IsNullOrWhiteSpace(revision.ChangeReason) ? summary : $"{summary}: {revision.ChangeReason}",
            ("Part", part.Id));

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
            revision.CreatedAt,
            creator?.GetDisplayName());
    }
}
