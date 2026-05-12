using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Definitions;

/// <summary>
/// Workflow Pattern Phase 3 — admin update endpoint. <c>DefinitionId</c> /
/// <c>EntityType</c> are immutable; the route id wins. Per Q2, in-flight
/// runs stay pinned on the version they started with — updates here only
/// affect new runs that haven't started yet.
/// </summary>
public record UpdateWorkflowDefinitionCommand(string DefinitionId, UpsertWorkflowDefinitionRequestModel Body)
    : IRequest<WorkflowDefinitionResponseModel>;

public class UpdateWorkflowDefinitionValidator : AbstractValidator<UpdateWorkflowDefinitionCommand>
{
    public UpdateWorkflowDefinitionValidator()
    {
        RuleFor(x => x.Body.DefaultMode)
            .NotEmpty()
            .Must(m => m == "express" || m == "guided");
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

public class UpdateWorkflowDefinitionHandler(AppDbContext db)
    : IRequestHandler<UpdateWorkflowDefinitionCommand, WorkflowDefinitionResponseModel>
{
    public async Task<WorkflowDefinitionResponseModel> Handle(
        UpdateWorkflowDefinitionCommand request, CancellationToken ct)
    {
        var row = await db.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.DefinitionId == request.DefinitionId, ct)
            ?? throw new KeyNotFoundException($"Workflow definition '{request.DefinitionId}' not found.");

        row.DefaultMode = request.Body.DefaultMode;
        row.StepsJson = request.Body.StepsJson;
        row.ExpressTemplateComponent = request.Body.ExpressTemplateComponent;

        await db.SaveChangesAsync(ct);

        return new WorkflowDefinitionResponseModel(
            row.Id, row.DefinitionId, row.EntityType, row.DefaultMode,
            row.StepsJson, row.ExpressTemplateComponent, row.IsSeedData);
    }
}
