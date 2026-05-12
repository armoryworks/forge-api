using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Definitions;

/// <summary>Workflow Pattern Phase 3 — admin create endpoint for workflow definitions.</summary>
public record CreateWorkflowDefinitionCommand(UpsertWorkflowDefinitionRequestModel Body)
    : IRequest<WorkflowDefinitionResponseModel>;

public class CreateWorkflowDefinitionValidator : AbstractValidator<CreateWorkflowDefinitionCommand>
{
    public CreateWorkflowDefinitionValidator()
    {
        RuleFor(x => x.Body.DefinitionId)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[a-z][a-z0-9-]*$")
            .WithMessage("definitionId must be lower-kebab-case (recommended: include a -vN suffix).");
        RuleFor(x => x.Body.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body.DefaultMode)
            .NotEmpty()
            .Must(m => m == "express" || m == "guided")
            .WithMessage("defaultMode must be 'express' or 'guided'.");
        RuleFor(x => x.Body.StepsJson)
            .NotEmpty()
            .Must(BeWellFormedStepsJson)
            .WithMessage("stepsJson must be a JSON array of step objects.")
            .MaximumLength(64 * 1024);
        RuleFor(x => x.Body.ExpressTemplateComponent)
            .MaximumLength(128)
            .When(x => x.Body.ExpressTemplateComponent is not null);
    }

    private static bool BeWellFormedStepsJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException) { return false; }
    }
}

public class CreateWorkflowDefinitionHandler(AppDbContext db)
    : IRequestHandler<CreateWorkflowDefinitionCommand, WorkflowDefinitionResponseModel>
{
    public async Task<WorkflowDefinitionResponseModel> Handle(
        CreateWorkflowDefinitionCommand request, CancellationToken ct)
    {
        var body = request.Body;
        var existing = await db.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.DefinitionId == body.DefinitionId, ct);
        if (existing is not null)
            throw new InvalidOperationException(
                $"Workflow definition '{body.DefinitionId}' already exists.");

        var row = new WorkflowDefinition
        {
            DefinitionId = body.DefinitionId,
            EntityType = body.EntityType,
            DefaultMode = body.DefaultMode,
            StepsJson = body.StepsJson,
            ExpressTemplateComponent = body.ExpressTemplateComponent,
            IsSeedData = false,
        };
        db.WorkflowDefinitions.Add(row);
        await db.SaveChangesAsync(ct);

        return new WorkflowDefinitionResponseModel(
            row.Id, row.DefinitionId, row.EntityType, row.DefaultMode,
            row.StepsJson, row.ExpressTemplateComponent, row.IsSeedData);
    }
}
