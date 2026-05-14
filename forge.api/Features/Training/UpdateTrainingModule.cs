using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record UpdateTrainingModuleCommand(
    int Id,
    string Title,
    string Slug,
    string Summary,
    TrainingContentType ContentType,
    string ContentJson,
    string? CoverImageUrl,
    int EstimatedMinutes,
    string[] Tags,
    string[] AppRoutes,
    bool IsPublished,
    bool IsOnboardingRequired,
    int SortOrder) : IRequest<TrainingModuleDetailResponseModel>;

public class UpdateTrainingModuleValidator : AbstractValidator<UpdateTrainingModuleCommand>
{
    public UpdateTrainingModuleValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.ContentJson).NotEmpty();
        RuleFor(x => x.EstimatedMinutes).GreaterThanOrEqualTo(0);
    }
}

public class UpdateTrainingModuleHandler(AppDbContext db)
    : IRequestHandler<UpdateTrainingModuleCommand, TrainingModuleDetailResponseModel>
{
    public async Task<TrainingModuleDetailResponseModel> Handle(
        UpdateTrainingModuleCommand request, CancellationToken ct)
    {
        var module = await db.TrainingModules.FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Training module {request.Id} not found.");

        module.Title = request.Title;
        module.Slug = request.Slug;
        module.Summary = request.Summary;
        module.ContentType = request.ContentType;
        module.ContentJson = request.ContentJson;
        module.CoverImageUrl = request.CoverImageUrl;
        module.EstimatedMinutes = request.EstimatedMinutes;
        module.Tags = JsonSerializer.Serialize(request.Tags);
        module.AppRoutes = JsonSerializer.Serialize(request.AppRoutes);
        module.IsPublished = request.IsPublished;
        module.IsOnboardingRequired = request.IsOnboardingRequired;
        module.SortOrder = request.SortOrder;

        await db.SaveChangesAsync(ct);

        return new TrainingModuleDetailResponseModel(
            module.Id,
            module.Title,
            module.Slug,
            module.Summary,
            module.ContentType,
            module.CoverImageUrl,
            module.EstimatedMinutes,
            request.Tags,
            module.IsPublished,
            module.IsOnboardingRequired,
            module.SortOrder,
            null,
            null,
            null,
            module.ContentJson,
            request.AppRoutes,
            module.CreatedAt,
            module.UpdatedAt
        );
    }
}
