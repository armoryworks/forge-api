namespace Forge.Core.Models;

/// <summary>
/// An over/under quantity tolerance in percent of the ordered quantity. A line is complete once it reaches
/// <see cref="MinComplete"/> and may go up to <see cref="MaxAccepted"/> without an override.
/// </summary>
public readonly record struct QuantityTolerance(decimal OverPercent, decimal UnderPercent)
{
    public static QuantityTolerance None { get; } = new(0m, 0m);

    public decimal MaxAccepted(decimal ordered) => ordered * (1m + OverPercent / 100m);

    public decimal MinComplete(decimal ordered) => ordered * (1m - UnderPercent / 100m);

    public bool Completes(decimal ordered, decimal done) => done >= MinComplete(ordered);

    public bool Exceeds(decimal ordered, decimal done) => done > MaxAccepted(ordered);
}
