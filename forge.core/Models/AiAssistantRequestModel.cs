namespace Forge.Core.Models;

public record AiAssistantRequestModel(
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    string? Category,
    string SystemPrompt,
    List<string>? AllowedEntityTypes,
    List<string>? StarterQuestions,
    bool IsActive = true,
    int SortOrder = 0,
    double Temperature = 0.7,
    int MaxContextChunks = 5);
