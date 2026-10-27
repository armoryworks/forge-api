using Forge.Core.Models;

namespace Forge.Api.Services.Cpq;

/// <summary>
/// What one set of selections on a configurator comes to: the errors that stop it, the unit price in the
/// functional currency with its breakdown, and the components and routing steps the options add to the base part.
/// </summary>
public sealed record CpqEvaluation
{
    public IReadOnlyList<string> Errors { get; init; } = [];
    public bool IsValid => Errors.Count == 0;
    public decimal Quantity { get; init; } = 1m;
    public decimal UnitPrice { get; init; }
    public decimal ExtendedPrice { get; init; }
    public int LeadTimeDays { get; init; }
    public IReadOnlyList<CpqPriceBreakdown> Breakdown { get; init; } = [];
    public IReadOnlyList<Component> Components { get; init; } = [];
    public IReadOnlyList<RoutingStep> RoutingSteps { get; init; } = [];
    public IReadOnlyDictionary<string, string> Selections { get; init; } = new Dictionary<string, string>();

    public sealed record Component(string OptionName, int? PartId, string? PartNumber, decimal Quantity);

    public sealed record RoutingStep(
        string OptionName, string Title, int? WorkCenterId, decimal SetupMinutes, decimal RunMinutesEach);
}
