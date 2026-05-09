using Riok.Mapperly.Abstractions;

using QBEngineer.Core.Entities;
using QBEngineer.Core.Models;

namespace QBEngineer.Api.Mappers;

[Mapper]
public static partial class LeadMapper
{
    [MapperIgnoreSource(nameof(Lead.ConvertedCustomer))]
    [MapperIgnoreSource(nameof(Lead.CreatedBy))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.DeletedAt))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.DeletedBy))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.IsDeleted))]
    // Phase 1j — engagement signals are computed from ActivityLog, not
    // mapped from the Lead entity. The repository projection populates
    // them; Mapperly leaves them at their record-default values
    // (LastActivityAt=null, RecentEngagementCount=0, IsStale=false).
    [MapperIgnoreTarget(nameof(LeadResponseModel.LastActivityAt))]
    [MapperIgnoreTarget(nameof(LeadResponseModel.RecentEngagementCount))]
    [MapperIgnoreTarget(nameof(LeadResponseModel.IsStale))]
    public static partial LeadResponseModel ToResponseModel(this Lead lead);
}
