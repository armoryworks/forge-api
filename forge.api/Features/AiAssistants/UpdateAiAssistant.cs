using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.AiAssistants;

public record UpdateAiAssistantCommand(int Id, AiAssistantRequestModel Data) : IRequest<AiAssistantResponseModel>;

public class UpdateAiAssistantValidator : AbstractValidator<UpdateAiAssistantCommand>
{
    public UpdateAiAssistantValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Data.SystemPrompt).NotEmpty().MaximumLength(50000);
        RuleFor(x => x.Data.Temperature).InclusiveBetween(0.0, 1.0);
        RuleFor(x => x.Data.MaxContextChunks).InclusiveBetween(1, 20);
    }
}

public class UpdateAiAssistantHandler(AppDbContext db) : IRequestHandler<UpdateAiAssistantCommand, AiAssistantResponseModel>
{
    public async Task<AiAssistantResponseModel> Handle(UpdateAiAssistantCommand request, CancellationToken ct)
    {
        var entity = await db.AiAssistants
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"AI Assistant with ID {request.Id} not found.");

        var d = request.Data;
        entity.Name = d.Name;
        entity.Description = d.Description ?? string.Empty;
        entity.Icon = d.Icon ?? entity.Icon;
        entity.Color = d.Color ?? entity.Color;
        entity.Category = d.Category ?? entity.Category;
        entity.SystemPrompt = d.SystemPrompt;
        entity.AllowedEntityTypes = JsonSerializer.Serialize(d.AllowedEntityTypes ?? []);
        entity.StarterQuestions = JsonSerializer.Serialize(d.StarterQuestions ?? []);
        entity.IsActive = d.IsActive;
        entity.SortOrder = d.SortOrder;
        entity.Temperature = d.Temperature;
        entity.MaxContextChunks = d.MaxContextChunks;

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
