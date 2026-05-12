using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ReviewCycleResponseModel(
    int Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    ReviewCycleStatus Status,
    string? Description,
    int ReviewCount);
