
namespace Forge.Core.Models;

public record TrainingPathModuleResponseModel(
    int ModuleId,
    string Title,
    TrainingContentType ContentType,
    int EstimatedMinutes,
    int Position,
    bool IsRequired,
    TrainingProgressStatus? MyStatus
);

public record TrainingPathResponseModel(
    int Id,
    string Title,
    string Slug,
    string Description,
    string Icon,
    bool IsAutoAssigned,
    bool IsActive,
    TrainingPathModuleResponseModel[] Modules
);
