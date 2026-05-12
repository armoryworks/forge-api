using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.AiAssistants;

public record GetAiAssistantQuery(int Id) : IRequest<AiAssistantResponseModel>;

public class GetAiAssistantHandler(AppDbContext db) : IRequestHandler<GetAiAssistantQuery, AiAssistantResponseModel>
{
    public async Task<AiAssistantResponseModel> Handle(GetAiAssistantQuery request, CancellationToken ct)
    {
        var a = await db.AiAssistants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"AI Assistant with ID {request.Id} not found.");

        return new AiAssistantResponseModel(
            a.Id, a.Name, a.Description, a.Icon, a.Color, a.Category,
            a.SystemPrompt,
            JsonSerializer.Deserialize<List<string>>(a.AllowedEntityTypes) ?? [],
            JsonSerializer.Deserialize<List<string>>(a.StarterQuestions) ?? [],
            a.IsActive, a.IsBuiltIn, a.SortOrder, a.Temperature, a.MaxContextChunks
        );
    }
}
