namespace Forge.Core.Models;

public record AiIndexStatusResponseModel(
    List<AiIndexEntityStatusModel> EntityTypes,
    int TotalChunks,
    DateTimeOffset? LastIndexedAt);
