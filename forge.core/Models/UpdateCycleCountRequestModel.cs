namespace Forge.Core.Models;

public record UpdateCycleCountRequestModel(
    string? Status,
    string? Notes,
    List<UpdateCycleCountLineModel>? Lines);
