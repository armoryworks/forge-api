
namespace Forge.Core.Models;

public record UpdateComplianceFormTemplateRequestModel(
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
    string ProfileCompletionKey,
    int? DocuSealTemplateId,
    string? AcroFieldMapJson);
