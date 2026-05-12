namespace Forge.Core.Models;

public record UserComplianceDetailResponseModel(
    int UserId,
    string UserName,
    string UserEmail,
    List<ComplianceFormSubmissionResponseModel> Submissions,
    List<IdentityDocumentResponseModel> IdentityDocuments,
    StateWithholdingInfoModel? StateWithholdingInfo);

public record StateWithholdingInfoModel(
    string StateCode,
    string StateName,
    string Category,
    string? FormName,
    string Source);
