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
    public static partial LeadResponseModel ToResponseModel(this Lead lead);
}
