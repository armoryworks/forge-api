namespace Forge.Core.Models;

public record TrackTypeResponseModel(
    int Id,
    string Name,
    string Code,
    string? Description,
    bool IsDefault,
    int SortOrder,
    List<StageResponseModel> Stages);
