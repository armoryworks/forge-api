using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.AiAssistants;

public record GetAllAiAssistantsQuery : IRequest<List<AiAssistantResponseModel>>;

public class GetAllAiAssistantsHandler(AppDbContext db) : IRequestHandler<GetAllAiAssistantsQuery, List<AiAssistantResponseModel>>
{
    public async Task<List<AiAssistantResponseModel>> Handle(GetAllAiAssistantsQuery request, CancellationToken ct)
    {
        var assistants = await db.AiAssistants
            .AsNoTracking()
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.Name)
            .ToListAsync(ct);

        return assistants.Select(a => new AiAssistantResponseModel(
            a.Id, a.Name, a.Description, a.Icon, a.Color, a.Category,
            a.SystemPrompt,
            JsonSerializer.Deserialize<List<string>>(a.AllowedEntityTypes) ?? [],
            JsonSerializer.Deserialize<List<string>>(a.StarterQuestions) ?? [],
            a.IsActive, a.IsBuiltIn, a.SortOrder, a.Temperature, a.MaxContextChunks
        )).ToList();
    }
}
