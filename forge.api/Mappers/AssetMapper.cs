using Riok.Mapperly.Abstractions;

using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Mappers;

[Mapper]
public static partial class AssetMapper
{
    [MapperIgnoreSource(nameof(BaseAuditableEntity.DeletedAt))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.DeletedBy))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.IsDeleted))]
    [MapperIgnoreSource(nameof(Asset.IsActiveForNewTransactions))]
    [MapProperty(nameof(Asset.SourceJob) + "." + nameof(Job.JobNumber), nameof(AssetResponseModel.SourceJobNumber))]
    [MapProperty(nameof(Asset.SourcePart) + "." + nameof(Part.PartNumber), nameof(AssetResponseModel.SourcePartNumber))]
    public static partial AssetResponseModel ToResponseModel(this Asset asset);
}
