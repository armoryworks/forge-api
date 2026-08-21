using Forge.Core.Enums;

namespace Forge.Core.Costing;

/// <summary>The eight standard cost elements as a value object — the unit of the cost roll. Supports
/// element-wise addition and scalar multiplication so rolling up sub-assemblies stays arithmetic.</summary>
public sealed record CostElementAmounts(
    decimal Mat,
    decimal Moh,
    decimal Lab,
    decimal Loh,
    decimal Mch,
    decimal Mohv,
    decimal Mohf,
    decimal Sub)
{
    /// <summary>All-zero amounts.</summary>
    public static readonly CostElementAmounts Zero = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Sum of all eight elements.</summary>
    public decimal Total => Mat + Moh + Lab + Loh + Mch + Mohv + Mohf + Sub;

    /// <summary>Element-wise sum.</summary>
    public static CostElementAmounts operator +(CostElementAmounts a, CostElementAmounts b) => new(
        a.Mat + b.Mat, a.Moh + b.Moh, a.Lab + b.Lab, a.Loh + b.Loh,
        a.Mch + b.Mch, a.Mohv + b.Mohv, a.Mohf + b.Mohf, a.Sub + b.Sub);

    /// <summary>Scalar multiply (e.g. a sub-assembly's rolled-up cost × quantity-per).</summary>
    public static CostElementAmounts operator *(CostElementAmounts a, decimal q) => new(
        a.Mat * q, a.Moh * q, a.Lab * q, a.Loh * q, a.Mch * q, a.Mohv * q, a.Mohf * q, a.Sub * q);

    /// <summary>Returns a copy with <paramref name="amount"/> added to a single element.</summary>
    public CostElementAmounts Add(CostElement element, decimal amount) => element switch
    {
        CostElement.Mat => this with { Mat = Mat + amount },
        CostElement.Moh => this with { Moh = Moh + amount },
        CostElement.Lab => this with { Lab = Lab + amount },
        CostElement.Loh => this with { Loh = Loh + amount },
        CostElement.Mch => this with { Mch = Mch + amount },
        CostElement.Mohv => this with { Mohv = Mohv + amount },
        CostElement.Mohf => this with { Mohf = Mohf + amount },
        CostElement.Sub => this with { Sub = Sub + amount },
        _ => this,
    };
}
