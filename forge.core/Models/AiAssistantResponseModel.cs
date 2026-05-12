namespace Forge.Core.Models;

public record AiAssistantResponseModel(
    int Id,
    string Name,
    string Description,
    string Icon,
    string Color,
    string Category,
    string SystemPrompt,
    List<string> AllowedEntityTypes,
    List<string> StarterQuestions,
    bool IsActive,
    bool IsBuiltIn,
    int SortOrder,
    double Temperature,
    int MaxContextChunks);
