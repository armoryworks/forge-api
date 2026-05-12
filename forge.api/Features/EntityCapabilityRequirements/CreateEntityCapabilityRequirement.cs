using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EntityCapabilityRequirements;

/// <summary>
/// Admin create. (EntityType, CapabilityCode, RequirementId) is unique-
/// indexed; collisions throw InvalidOperationException → 409 via global
/// middleware. Predicate JSON is well-formedness-checked but not deeply
/// validated — invalid predicates evaluate to false at chip time and
/// surface as a logged warning.
/// </summary>
public record CreateEntityCapabilityRequirementCommand(UpsertEntityCapabilityRequirementRequestModel Body)
    : IRequest<EntityCapabilityRequirementResponseModel>;

public class CreateEntityCapabilityRequirementValidator
    : AbstractValidator<CreateEntityCapabilityRequirementCommand>
{
    public CreateEntityCapabilityRequirementValidator()
    {
        RuleFor(x => x.Body.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body.CapabilityCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body.RequirementId)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[a-zA-Z][a-zA-Z0-9_-]*$")
            .WithMessage("requirementId must be camelCase or kebab-case (letters, digits, underscore, hyphen).");
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

public class CreateEntityCapabilityRequirementHandler(AppDbContext db)
    : IRequestHandler<CreateEntityCapabilityRequirementCommand, EntityCapabilityRequirementResponseModel>
{
    public async Task<EntityCapabilityRequirementResponseModel> Handle(
        CreateEntityCapabilityRequirementCommand request,
        CancellationToken cancellationToken)
    {
        var body = request.Body;

        var existing = await db.EntityCapabilityRequirements
            .FirstOrDefaultAsync(
                r => r.EntityType == body.EntityType
                  && r.CapabilityCode == body.CapabilityCode
                  && r.RequirementId == body.RequirementId,
                cancellationToken);
        if (existing is not null)
            throw new InvalidOperationException(
                $"Requirement '{body.RequirementId}' already exists for {body.EntityType}/{body.CapabilityCode}.");

        var row = new EntityCapabilityRequirement
        {
            EntityType = body.EntityType,
            CapabilityCode = body.CapabilityCode,
            RequirementId = body.RequirementId,
            Predicate = body.Predicate,
            DisplayNameKey = body.DisplayNameKey,
            MissingMessageKey = body.MissingMessageKey,
            SortOrder = body.SortOrder,
            IsSeedData = false,
        };
        db.EntityCapabilityRequirements.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return new EntityCapabilityRequirementResponseModel(
            row.Id, row.EntityType, row.CapabilityCode, row.RequirementId,
            row.Predicate, row.DisplayNameKey, row.MissingMessageKey,
            row.SortOrder, row.IsSeedData);
    }
}
