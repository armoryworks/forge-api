using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateComplianceFormTemplateRequestModel(
    string Name,
    ComplianceFormType FormType,
    string Description,
    string Icon,
    string? SourceUrl,
    bool IsAutoSync,
    bool IsActive,
    int SortOrder,
    bool RequiresIdentityDocs,
    bool BlocksJobAssignment,
    string ProfileCompletionKey);
