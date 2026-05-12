using System.Text.Json;

using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.AiAssistants;

public record CreateAiAssistantCommand(AiAssistantRequestModel Data) : IRequest<AiAssistantResponseModel>;

public class CreateAiAssistantValidator : AbstractValidator<CreateAiAssistantCommand>
{
    public CreateAiAssistantValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Data.SystemPrompt).NotEmpty().MaximumLength(50000);
        RuleFor(x => x.Data.Temperature).InclusiveBetween(0.0, 1.0);
        RuleFor(x => x.Data.MaxContextChunks).InclusiveBetween(1, 20);
    }
}

public class CreateAiAssistantHandler(AppDbContext db) : IRequestHandler<CreateAiAssistantCommand, AiAssistantResponseModel>
{
    public async Task<AiAssistantResponseModel> Handle(CreateAiAssistantCommand request, CancellationToken ct)
    {
        var d = request.Data;
        var entity = new AiAssistant
        {
            Name = d.Name,
            Description = d.Description ?? string.Empty,
            Icon = d.Icon ?? "smart_toy",
            Color = d.Color ?? "#0d9488",
            Category = d.Category ?? "Custom",
            SystemPrompt = d.SystemPrompt,
            AllowedEntityTypes = JsonSerializer.Serialize(d.AllowedEntityTypes ?? []),
            StarterQuestions = JsonSerializer.Serialize(d.StarterQuestions ?? []),
            IsActive = d.IsActive,
            IsBuiltIn = false,
            SortOrder = d.SortOrder,
            Temperature = d.Temperature,
            MaxContextChunks = d.MaxContextChunks,
        };

        db.AiAssistants.Add(entity);
        await db.SaveChangesAsync(ct);

        return new AiAssistantResponseModel(
            entity.Id, entity.Name, entity.Description, entity.Icon, entity.Color, entity.Category,
            entity.SystemPrompt,
            d.AllowedEntityTypes ?? [],
            d.StarterQuestions ?? [],
            entity.IsActive, entity.IsBuiltIn, entity.SortOrder, entity.Temperature, entity.MaxContextChunks
        );
    }
}
