using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IUomService
{
    Task<decimal> ConvertAsync(decimal quantity, int fromUomId, int toUomId, int? partId, CancellationToken ct);
    Task<decimal?> TryConvertAsync(decimal quantity, int fromUomId, int toUomId, int? partId, CancellationToken ct);
    Task<IReadOnlyList<UomConversion>> GetConversionsAsync(int uomId, CancellationToken ct);
    Task<IReadOnlyList<UnitOfMeasure>> GetByCategoryAsync(UomCategory category, CancellationToken ct);
}
