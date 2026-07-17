namespace Forge.Core.Models;

public record CheckpointResponseModel(string WorldId, string Blob, DateTimeOffset UpdatedAt);
