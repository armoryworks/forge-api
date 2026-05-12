using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Validators;

/// <summary>
/// Workflow Pattern Phase 3 — Admin update endpoint for entity readiness
/// validators. EntityType and ValidatorId are immutable on update; the
/// route ids win.
/// </summary>
public record UpdateEntityValidatorCommand(int Id, UpsertEntityValidatorRequestModel Body)
    : IRequest<EntityValidatorResponseModel>;

public class UpdateEntityValidatorValidator : AbstractValidator<UpdateEntityValidatorCommand>
{
    public UpdateEntityValidatorValidator()
    {
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

public class UpdateEntityValidatorHandler(AppDbContext db)
    : IRequestHandler<UpdateEntityValidatorCommand, EntityValidatorResponseModel>
{
    public async Task<EntityValidatorResponseModel> Handle(
        UpdateEntityValidatorCommand request,
        CancellationToken cancellationToken)
    {
        var row = await db.EntityReadinessValidators
            .FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Validator id {request.Id} not found.");

        row.Predicate = request.Body.Predicate;
        row.DisplayNameKey = request.Body.DisplayNameKey;
        row.MissingMessageKey = request.Body.MissingMessageKey;
        await db.SaveChangesAsync(cancellationToken);

        return new EntityValidatorResponseModel(
            row.Id, row.EntityType, row.ValidatorId, row.Predicate,
            row.DisplayNameKey, row.MissingMessageKey, row.IsSeedData);
    }
}
