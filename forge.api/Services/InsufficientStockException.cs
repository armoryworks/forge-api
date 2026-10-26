namespace Forge.Api.Services;

/// <summary>
/// Thrown when a shipment is refused because one or more of its stocked lines lack the free stock to relieve.
/// Nothing is drawn and the shipment is left as it was.
/// </summary>
public class InsufficientStockException(string message, IReadOnlyList<ShortStockLine> lines)
    : InvalidOperationException(message)
{
    public IReadOnlyList<ShortStockLine> Lines { get; } = lines;
}
