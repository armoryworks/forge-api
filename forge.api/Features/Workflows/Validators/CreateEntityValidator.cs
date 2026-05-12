using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Validators;

/// <summary>
/// Workflow Pattern Phase 3 — Admin create endpoint for entity readiness
/// validators. The (entityType, validatorId) tuple is unique-indexed; collisions
/// surface as a 409 by way of the global exception middleware.
/// </summary>
public record CreateEntityValidatorCommand(UpsertEntityValidatorRequestModel Body)
    : IRequest<EntityValidatorResponseModel>;

public class CreateEntityValidatorValidator : AbstractValidator<CreateEntityValidatorCommand>
{
    public CreateEntityValidatorValidator()
    {
        RuleFor(x => x.Body.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body.ValidatorId)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[a-zA-Z][a-zA-Z0-9_-]*$")
            .WithMessage("validatorId must be camelCase (letters, digits, underscore, hyphen).");
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

public class CreateEntityValidatorHandler(AppDbContext db)
    : IRequestHandler<CreateEntityValidatorCommand, EntityValidatorResponseModel>
{
    public async Task<EntityValidatorResponseModel> Handle(
        CreateEntityValidatorCommand request,
        CancellationToken cancellationToken)
    {
        var body = request.Body;

        var existing = await db.EntityReadinessValidators
            .FirstOrDefaultAsync(
                v => v.EntityType == body.EntityType && v.ValidatorId == body.ValidatorId,
                cancellationToken);
        if (existing is not null)
            throw new InvalidOperationException(
                $"Validator '{body.ValidatorId}' already exists for entity type '{body.EntityType}'.");

        var row = new EntityReadinessValidator
        {
            EntityType = body.EntityType,
            ValidatorId = body.ValidatorId,
            Predicate = body.Predicate,
            DisplayNameKey = body.DisplayNameKey,
            MissingMessageKey = body.MissingMessageKey,
            IsSeedData = false,
        };
        db.EntityReadinessValidators.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return new EntityValidatorResponseModel(
            row.Id, row.EntityType, row.ValidatorId, row.Predicate,
            row.DisplayNameKey, row.MissingMessageKey, row.IsSeedData);
    }
}
