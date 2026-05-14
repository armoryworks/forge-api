using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockUomService(ILogger<MockUomService> logger) : IUomService
{
    public Task<decimal> ConvertAsync(decimal quantity, int fromUomId, int toUomId, int? partId, CancellationToken ct)
    {
        logger.LogInformation("MockUomService: Convert {Quantity} from UOM {From} to UOM {To}", quantity, fromUomId, toUomId);

        if (fromUomId == toUomId)
            return Task.FromResult(quantity);

        // Return identity conversion in mock mode
        return Task.FromResult(quantity);
    }

    public Task<decimal?> TryConvertAsync(decimal quantity, int fromUomId, int toUomId, int? partId, CancellationToken ct)
    {
        logger.LogInformation("MockUomService: TryConvert {Quantity} from UOM {From} to UOM {To}", quantity, fromUomId, toUomId);

        if (fromUomId == toUomId)
            return Task.FromResult<decimal?>(quantity);

        return Task.FromResult<decimal?>(quantity);
    }

    public Task<IReadOnlyList<UomConversion>> GetConversionsAsync(int uomId, CancellationToken ct)
    {
        logger.LogInformation("MockUomService: GetConversions for UOM {UomId}", uomId);
        return Task.FromResult<IReadOnlyList<UomConversion>>(Array.Empty<UomConversion>());
    }

    public Task<IReadOnlyList<UnitOfMeasure>> GetByCategoryAsync(UomCategory category, CancellationToken ct)
    {
        logger.LogInformation("MockUomService: GetByCategory {Category}", category);
        return Task.FromResult<IReadOnlyList<UnitOfMeasure>>(Array.Empty<UnitOfMeasure>());
    }
}
