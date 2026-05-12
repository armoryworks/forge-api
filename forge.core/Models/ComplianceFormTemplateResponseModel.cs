using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ComplianceFormTemplateResponseModel(
    int Id,
    string Name,
    ComplianceFormType FormType,
    string Description,
    string Icon,
    string? SourceUrl,
    bool IsAutoSync,
    bool IsActive,
    int SortOrder,
    bool RequiresIdentityDocs,
    int? DocuSealTemplateId,
    DateTimeOffset? LastSyncedAt,
    int? ManualOverrideFileId,
    bool BlocksJobAssignment,
    string ProfileCompletionKey,
    int? CurrentFormDefinitionVersionId,
    string? FormDefinitionJson,
    string? FormDefinitionRevision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? AcroFieldMapJson,
    int? FilledPdfTemplateId);
