using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GenerateWalkthroughStepsCommand(
    int ModuleId,
    string JwtToken) : IRequest<GenerateWalkthroughResponseModel>;

public class GenerateWalkthroughStepsHandler(
    AppDbContext db,
    IWalkthroughGeneratorService walkthroughGenerator,
    ILogger<GenerateWalkthroughStepsHandler> logger)
    : IRequestHandler<GenerateWalkthroughStepsCommand, GenerateWalkthroughResponseModel>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<GenerateWalkthroughResponseModel> Handle(
        GenerateWalkthroughStepsCommand request,
        CancellationToken ct)
    {
        var module = await db.TrainingModules
            .FirstOrDefaultAsync(m => m.Id == request.ModuleId && m.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Training module {request.ModuleId} not found.");

        if (module.ContentType != Forge.Core.Enums.TrainingContentType.Walkthrough)
            throw new InvalidOperationException(
                $"Module {request.ModuleId} is type '{module.ContentType}' — only Walkthrough modules can have AI-generated steps.");

        // Resolve target app route from the module's AppRoutes JSON array
        var appRoutes = JsonSerializer.Deserialize<string[]>(module.AppRoutes ?? "[]") ?? [];
        var targetRoute = appRoutes.Length > 0 ? appRoutes[0] : "/dashboard";

        logger.LogInformation(
            "Generating walkthrough steps for module {ModuleId} '{Title}' (route: {Route})",
            request.ModuleId, module.Title, targetRoute);

        var steps = await walkthroughGenerator.GenerateStepsAsync(
            targetRoute, request.ModuleId, request.JwtToken, ct);

        // Preserve any existing non-step fields in contentJson (e.g. appRoute, startButtonLabel)
        // by merging rather than replacing the whole object.
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

        // Build merged content: keep existing fields, overwrite steps
        var merged = new Dictionary<string, object?>();
        foreach (var kv in existing)
            merged[kv.Key] = kv.Value;
        merged["steps"] = steps;

        // Also ensure appRoute is present for the resume path (AppComponent walkthrough detection)
        if (!merged.ContainsKey("appRoute"))
            merged["appRoute"] = targetRoute;

        module.ContentJson = JsonSerializer.Serialize(merged, JsonOptions);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Walkthrough generation complete: {StepCount} steps saved for module {ModuleId}",
            steps.Count, request.ModuleId);

        return new GenerateWalkthroughResponseModel(
            request.ModuleId,
            steps.Count,
            steps,
            module.ContentJson);
    }
}
