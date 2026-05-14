
namespace Forge.Core.Models;

public record ComplianceFormSubmissionResponseModel(
    int Id,
    int TemplateId,
    string TemplateName,
    ComplianceFormType FormType,
    ComplianceSubmissionStatus Status,
    DateTimeOffset? SignedAt,
    int? SignedPdfFileId,
    string? DocuSealSubmitUrl,
    string? FormDataJson,
    int? FormDefinitionVersionId,
    DateTimeOffset CreatedAt,
    int? FilledPdfFileId,
    DateTimeOffset? I9Section1SignedAt,
    DateTimeOffset? I9Section2SignedAt,
    int? I9EmployerUserId,
    string? I9DocumentListType,
    string? I9DocumentDataJson,
    DateTimeOffset? I9Section2OverdueAt,
    DateTimeOffset? I9ReverificationDueAt);
