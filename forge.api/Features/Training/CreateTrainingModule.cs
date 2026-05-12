using System.Text.Json;

using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record CreateTrainingModuleCommand(
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
    int SortOrder,
    int CreatedByUserId) : IRequest<TrainingModuleDetailResponseModel>;

public class CreateTrainingModuleValidator : AbstractValidator<CreateTrainingModuleCommand>
{
    public CreateTrainingModuleValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.ContentJson).NotEmpty();
        RuleFor(x => x.EstimatedMinutes).GreaterThanOrEqualTo(0);
    }
}

public class CreateTrainingModuleHandler(AppDbContext db)
    : IRequestHandler<CreateTrainingModuleCommand, TrainingModuleDetailResponseModel>
{
    public async Task<TrainingModuleDetailResponseModel> Handle(
        CreateTrainingModuleCommand request, CancellationToken ct)
    {
        var module = new TrainingModule
        {
            Title = request.Title,
            Slug = request.Slug,
            Summary = request.Summary,
            ContentType = request.ContentType,
            ContentJson = request.ContentJson,
            CoverImageUrl = request.CoverImageUrl,
            EstimatedMinutes = request.EstimatedMinutes,
            Tags = JsonSerializer.Serialize(request.Tags),
            AppRoutes = JsonSerializer.Serialize(request.AppRoutes),
            IsPublished = request.IsPublished,
            IsOnboardingRequired = request.IsOnboardingRequired,
            SortOrder = request.SortOrder,
            CreatedByUserId = request.CreatedByUserId,
        };

        db.TrainingModules.Add(module);
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
