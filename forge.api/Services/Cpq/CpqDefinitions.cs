using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forge.Api.Services.Cpq;

/// <summary>
/// The JSON shapes a configurator's admin-authored fields hold, read case-insensitively. See
/// <see cref="Forge.Core.Entities.ProductConfigurator"/> for the field-by-field schema.
/// </summary>
public static class CpqDefinitions
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    public enum AdderType
    {
        Fixed,
        PerUnit,
        Percent,
        PerValue,
    }

    public sealed record AllowedValue(string Value, string? Label = null, decimal? Number = null);

    public sealed record NumericRange(decimal? Min, decimal? Max);

    public sealed record PriceAdder(string? When, AdderType Type, decimal Amount);

    public sealed record LeadTimeAdder(string? When, int Days);

    public sealed record PricingRule(List<PriceAdder>? Adders, List<LeadTimeAdder>? LeadTimeDays);

    public sealed record ComponentImpact(string? When, int? PartId, string? PartNumber, decimal Quantity, bool PerValue = false);

    public sealed record RoutingImpact(
        string? When, string Title, int? WorkCenterId, decimal SetupMinutes = 0m, decimal RunMinutesEach = 0m);

    public sealed record Condition
    {
        public string Option { get; init; } = string.Empty;

        [JsonPropertyName("equals")]
        public string? EqualsValue { get; init; }

        public List<string>? In { get; init; }
        public bool? Selected { get; init; }
        public decimal? Min { get; init; }
        public decimal? Max { get; init; }
    }

    public sealed record ValidationRule(string Type, Condition If, Condition Then, string? Message = null);

    public sealed record PricingFormula(string? Expression);
}
