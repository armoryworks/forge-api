namespace Forge.Core.Models;

/// <summary>Start a run: by definition id, or by code (latest published version). Subject is optional.</summary>
public record StartSequenceRequestModel(int? DefinitionId, string? Code, string? SubjectEntityType, int? SubjectEntityId);
