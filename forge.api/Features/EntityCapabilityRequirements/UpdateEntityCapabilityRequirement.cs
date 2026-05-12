using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EntityCapabilityRequirements;

/// <summary>
/// Admin update by id. Mutating the natural-key fields (EntityType /
/// CapabilityCode / RequirementId) is permitted but uniqueness is still
/// enforced — collision throws InvalidOperationException → 409. Seeded
/// rows (none today, deferred per Dan's option-B choice) get a softer
/// guard if the catalog ever populates.
/// </summary>
public record UpdateEntityCapabilityRequirementCommand(int Id, UpsertEntityCapabilityRequirementRequestModel Body)
    : IRequest<EntityCapabilityRequirementResponseModel>;

public class UpdateEntityCapabilityRequirementValidator
    : AbstractValidator<UpdateEntityCapabilityRequirementCommand>
{
    public UpdateEntityCapabilityRequirementValidator()
    {
        RuleFor(x => x.Body.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body.CapabilityCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body.RequirementId)
            .NotEmpty().MaximumLength(64)
            .Matches("^[a-zA-Z][a-zA-Z0-9_-]*$");
        RuleFor(x => x.Body.Predicate)
            .NotEmpty()
            .Must(BeWellFormedJson).WithMessage("predicate must be valid JSON.")
            .MaximumLength(16 * 1024);
        RuleFor(x => x.Body.DisplayNameKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Body.MissingMessageKey).NotEmpty().MaximumLength(128);
    }

    private static bool BeWellFormedJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { using var _ = JsonDocument.Parse(value); return true; }
        catch (JsonException) { return false; }
    }
}

public class UpdateEntityCapabilityRequirementHandler(AppDbContext db)
    : IRequestHandler<UpdateEntityCapabilityRequirementCommand, EntityCapabilityRequirementResponseModel>
{
    public async Task<EntityCapabilityRequirementResponseModel> Handle(
        UpdateEntityCapabilityRequirementCommand request,
        CancellationToken cancellationToken)
    {
        var row = await db.EntityCapabilityRequirements
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"EntityCapabilityRequirement {request.Id} not found");

        var body = request.Body;

        // Collision guard if the natural-key tuple changed under us.
        if (row.EntityType != body.EntityType
            || row.CapabilityCode != body.CapabilityCode
            || row.RequirementId != body.RequirementId)
        {
            var clash = await db.EntityCapabilityRequirements
                .AnyAsync(
                    r => r.Id != request.Id
                      && r.EntityType == body.EntityType
                      && r.CapabilityCode == body.CapabilityCode
                      && r.RequirementId == body.RequirementId,
                    cancellationToken);
            if (clash)
                throw new InvalidOperationException(
                    $"Requirement '{body.RequirementId}' already exists for {body.EntityType}/{body.CapabilityCode}.");
        }

        row.EntityType = body.EntityType;
        row.CapabilityCode = body.CapabilityCode;
        row.RequirementId = body.RequirementId;
        row.Predicate = body.Predicate;
        row.DisplayNameKey = body.DisplayNameKey;
        row.MissingMessageKey = body.MissingMessageKey;
        row.SortOrder = body.SortOrder;

        await db.SaveChangesAsync(cancellationToken);

        return new EntityCapabilityRequirementResponseModel(
            row.Id, row.EntityType, row.CapabilityCode, row.RequirementId,
            row.Predicate, row.DisplayNameKey, row.MissingMessageKey,
            row.SortOrder, row.IsSeedData);
    }
}
