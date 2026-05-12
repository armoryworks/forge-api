namespace Forge.Core.Models;

public record ShippingPackage(
    decimal WeightLbs,
    decimal LengthIn,
    decimal WidthIn,
    decimal HeightIn);
