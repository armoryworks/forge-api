using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record UpdateWalkthroughStepsCommand(
    int ModuleId,
    List<WalkthroughStep> Steps) : IRequest<TrainingModuleDetailResponseModel>;

public class UpdateWalkthroughStepsValidator : AbstractValidator<UpdateWalkthroughStepsCommand>
{
    public UpdateWalkthroughStepsValidator()
    {
        RuleFor(x => x.ModuleId).GreaterThan(0);
        RuleFor(x => x.Steps).NotNull();
    }
}

public class UpdateWalkthroughStepsHandler(AppDbContext db)
    : IRequestHandler<UpdateWalkthroughStepsCommand, TrainingModuleDetailResponseModel>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<TrainingModuleDetailResponseModel> Handle(
        UpdateWalkthroughStepsCommand request, CancellationToken ct)
    {
        var module = await db.TrainingModules
            .FirstOrDefaultAsync(m => m.Id == request.ModuleId && m.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Training module {request.ModuleId} not found.");

        if (module.ContentType != TrainingContentType.Walkthrough)
            throw new InvalidOperationException(
                $"Module {request.ModuleId} is type '{module.ContentType}' — only Walkthrough modules support walkthrough steps.");

        // Preserve existing non-step fields (e.g. appRoute, startButtonLabel)
        var existing = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrWhiteSpace(module.ContentJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(module.ContentJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    existing[prop.Name] = prop.Value.Clone();
            }
            catch { /* malformed — start fresh */ }
        }

        // Merge: keep existing fields, overwrite steps
        var merged = new Dictionary<string, object?>();
        foreach (var kv in existing)
            merged[kv.Key] = kv.Value;
        merged["steps"] = request.Steps;

        module.ContentJson = JsonSerializer.Serialize(merged, JsonOptions);
        await db.SaveChangesAsync(ct);

        var tags = JsonSerializer.Deserialize<string[]>(module.Tags ?? "[]") ?? [];
        var appRoutes = JsonSerializer.Deserialize<string[]>(module.AppRoutes ?? "[]") ?? [];

        return new TrainingModuleDetailResponseModel(
            module.Id,
            module.Title,
            module.Slug,
            module.Summary,
            module.ContentType,
            module.CoverImageUrl,
            module.EstimatedMinutes,
            tags,
            module.IsPublished,
            module.IsOnboardingRequired,
            module.SortOrder,
            null,
            null,
            null,
            module.ContentJson,
            appRoutes,
            module.CreatedAt,
            module.UpdatedAt
        );
    }
}
