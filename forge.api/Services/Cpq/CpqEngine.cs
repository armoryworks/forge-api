using System.Globalization;
using System.Text.Json;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Services.Cpq;

/// <summary>
/// The configurator rule engine: resolves selections (defaults, option dependencies), validates them against
/// each option and the configurator's declarative rules, prices them and lists the components and routing
/// steps they add. Pure: it reads only the configurator it is given and never touches storage, so the same
/// selections always give the same answer. Selections are keyed by option name (case-insensitive) or option id.
/// </summary>
public static class CpqEngine
{
    public const string QuantityVariable = "quantity";
    public const string BasePriceVariable = "basePrice";
    private const int MaxTextLength = 500;

    public static CpqEvaluation Evaluate(
        ProductConfigurator configurator, IReadOnlyDictionary<string, string>? selections, decimal quantity)
    {
        var errors = new List<string>();
        if (quantity <= 0m)
        {
            errors.Add("Quantity must be greater than zero.");
            quantity = 1m;
        }

        var options = configurator.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).ToList();
        var raw = ResolveRaw(options, selections ?? new Dictionary<string, string>());
        var states = new List<OptionState>();
        foreach (var option in options)
        {
            var active = IsActive(option, options, raw);
            states.Add(ReadOption(option, active ? raw[option] : null, active, errors));
        }

        foreach (var rule in Parse<List<CpqDefinitions.ValidationRule>>(
                     configurator.ValidationRulesJson, "The configurator's validation rules are not valid JSON.", errors) ?? [])
            ApplyRule(rule, states, errors);

        var basePrice = configurator.BasePrice ?? 0m;
        var breakdown = new List<CpqPriceBreakdown>
        {
            new() { OptionName = "Base price", Selection = configurator.BasePart?.PartNumber ?? string.Empty, PriceImpact = basePrice },
        };
        var perUnit = 0m;
        var fixedTotal = 0m;
        var leadTime = 0;
        var components = new List<CpqEvaluation.Component>();
        var steps = new List<CpqEvaluation.RoutingStep>();

        foreach (var state in states.Where(s => s.HasValue))
        {
            var rule = ParsePricingRule(state.Option, errors);
            foreach (var adder in rule.Adders ?? [])
                foreach (var _ in MatchingValues(state, adder.When))
                {
                    var impact = adder.Type switch
                    {
                        CpqDefinitions.AdderType.Fixed => adder.Amount / quantity,
                        CpqDefinitions.AdderType.Percent => basePrice * adder.Amount / 100m,
                        CpqDefinitions.AdderType.PerValue => adder.Amount * state.Number,
                        _ => adder.Amount,
                    };
                    if (adder.Type == CpqDefinitions.AdderType.Fixed)
                        fixedTotal += adder.Amount;
                    else
                        perUnit += impact;
                    breakdown.Add(new CpqPriceBreakdown
                    {
                        OptionName = state.Option.Name,
                        Selection = state.Display,
                        PriceImpact = Math.Round(impact, 4, MidpointRounding.AwayFromZero),
                    });
                }

            foreach (var lead in rule.LeadTimeDays ?? [])
                leadTime += MatchingValues(state, lead.When).Count() * lead.Days;

            foreach (var impact in Parse<List<CpqDefinitions.ComponentImpact>>(
                         state.Option.BomImpactJson, $"Option '{state.Option.Name}' has a BOM impact that is not valid JSON.", errors) ?? [])
                foreach (var _ in MatchingValues(state, impact.When))
                {
                    var qty = impact.PerValue ? impact.Quantity * state.Number : impact.Quantity;
                    if (qty > 0m && (impact.PartId is not null || !string.IsNullOrWhiteSpace(impact.PartNumber)))
                        components.Add(new CpqEvaluation.Component(state.Option.Name, impact.PartId, impact.PartNumber?.Trim(), qty));
                }

            foreach (var impact in Parse<List<CpqDefinitions.RoutingImpact>>(
                         state.Option.RoutingImpactJson, $"Option '{state.Option.Name}' has a routing impact that is not valid JSON.", errors) ?? [])
                foreach (var _ in MatchingValues(state, impact.When))
                    if (!string.IsNullOrWhiteSpace(impact.Title))
                        steps.Add(new CpqEvaluation.RoutingStep(
                            state.Option.Name, impact.Title.Trim(), impact.WorkCenterId, impact.SetupMinutes, impact.RunMinutesEach));
        }

        var formula = EvaluateFormula(configurator.PricingFormulaJson, states, basePrice, quantity, errors);
        if (formula != 0m)
            breakdown.Add(new CpqPriceBreakdown
            {
                OptionName = "Pricing formula",
                Selection = string.Empty,
                PriceImpact = Math.Round(formula, 4, MidpointRounding.AwayFromZero),
            });

        var unitPrice = Math.Round(basePrice + perUnit + formula + fixedTotal / quantity, 4, MidpointRounding.AwayFromZero);
        if (unitPrice < 0m)
            errors.Add("The configured price comes to less than zero. Check the option price adders.");

        return new CpqEvaluation
        {
            Errors = errors,
            Quantity = quantity,
            UnitPrice = unitPrice,
            ExtendedPrice = Math.Round(unitPrice * quantity, 2, MidpointRounding.AwayFromZero),
            LeadTimeDays = Math.Max(0, leadTime),
            Breakdown = breakdown,
            Components = components,
            RoutingSteps = steps,
            Selections = states.Where(s => s.HasValue)
                .ToDictionary(s => s.Option.Name, s => s.Display, StringComparer.OrdinalIgnoreCase),
        };
    }

    private sealed record OptionState(
        ConfiguratorOption Option, bool Active, IReadOnlyList<string> Values, decimal Number, string Display)
    {
        public bool HasValue => Active && Values.Count > 0;
    }

    private static Dictionary<ConfiguratorOption, string?> ResolveRaw(
        List<ConfiguratorOption> options, IReadOnlyDictionary<string, string> selections)
    {
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in selections)
            byKey[key.Trim()] = value;

        var raw = new Dictionary<ConfiguratorOption, string?>();
        foreach (var option in options)
        {
            var value = byKey.TryGetValue(option.Name.Trim(), out var byName) ? byName
                : byKey.TryGetValue(option.Id.ToString(CultureInfo.InvariantCulture), out var byId) ? byId
                : null;
            raw[option] = string.IsNullOrWhiteSpace(value) ? option.DefaultValue?.Trim() : value.Trim();
        }
        return raw;
    }

    private static bool IsActive(
        ConfiguratorOption option, List<ConfiguratorOption> options, Dictionary<ConfiguratorOption, string?> raw)
    {
        if (string.IsNullOrWhiteSpace(option.DependsOnOptionId))
            return true;

        var spec = option.DependsOnOptionId.Trim();
        string? required = null;
        var eq = spec.IndexOf('=');
        if (eq >= 0)
        {
            required = spec[(eq + 1)..].Trim();
            spec = spec[..eq].Trim();
        }

        var parent = options.FirstOrDefault(o => string.Equals(o.Name.Trim(), spec, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault(o => o.Id.ToString(CultureInfo.InvariantCulture) == spec);
        if (parent is null || ReferenceEquals(parent, option))
            return true;

        var parentValues = SplitValues(parent, raw[parent]);
        return required is null
            ? parentValues.Count > 0
            : parentValues.Contains(required, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> SplitValues(ConfiguratorOption option, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        return option.OptionType switch
        {
            ConfiguratorOptionType.MultiSelect => SplitMulti(value),
            ConfiguratorOptionType.Checkbox => IsTrue(value) ? ["true"] : [],
            _ => [value.Trim()],
        };
    }

    private static List<string> SplitMulti(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('['))
        {
            var parsed = TryDeserialize<List<string>>(trimmed);
            if (parsed is not null)
                return parsed.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsTrue(string value) =>
        value.Trim().ToLowerInvariant() is "true" or "yes" or "1" or "on";

    private static OptionState ReadOption(ConfiguratorOption option, string? value, bool active, List<string> errors)
    {
        if (!active)
            return new OptionState(option, false, [], 0m, string.Empty);

        var values = SplitValues(option, value);
        if (values.Count == 0)
        {
            if (option.IsRequired)
                errors.Add($"{option.Name} is required.");
            return new OptionState(option, true, [], 0m, string.Empty);
        }

        var (allowed, range) = ParseAllowedValues(option, errors);
        decimal number = 0m;
        switch (option.OptionType)
        {
            case ConfiguratorOptionType.Numeric:
            case ConfiguratorOptionType.Quantity:
                if (!decimal.TryParse(values[0], NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                {
                    errors.Add($"{option.Name} must be a number.");
                    break;
                }
                if (option.OptionType == ConfiguratorOptionType.Quantity && (number < 0m || number != Math.Floor(number)))
                    errors.Add($"{option.Name} must be a whole number of zero or more.");
                if (range?.Min is decimal min && number < min)
                    errors.Add($"{option.Name} must be at least {min.ToString(CultureInfo.InvariantCulture)}.");
                if (range?.Max is decimal max && number > max)
                    errors.Add($"{option.Name} must be at most {max.ToString(CultureInfo.InvariantCulture)}.");
                break;
            case ConfiguratorOptionType.Checkbox:
                number = 1m;
                break;
            case ConfiguratorOptionType.Text:
                if (values[0].Length > MaxTextLength)
                    errors.Add($"{option.Name} must be {MaxTextLength} characters or fewer.");
                break;
            default:
                foreach (var v in values)
                    if (allowed.Count > 0 && !allowed.Any(a => string.Equals(a.Value, v, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"'{v}' is not a choice for {option.Name}.");
                if (option.OptionType == ConfiguratorOptionType.MultiSelect)
                    number = values.Count;
                else
                {
                    var match = allowed.FirstOrDefault(a => string.Equals(a.Value, values[0], StringComparison.OrdinalIgnoreCase));
                    number = match?.Number
                        ?? (decimal.TryParse(values[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m);
                }
                values = values
                    .Select(v => allowed.FirstOrDefault(a => string.Equals(a.Value, v, StringComparison.OrdinalIgnoreCase))?.Value ?? v)
                    .ToList();
                break;
        }

        return new OptionState(option, true, values, number, string.Join(", ", values));
    }

    private static (List<CpqDefinitions.AllowedValue> Allowed, CpqDefinitions.NumericRange? Range) ParseAllowedValues(
        ConfiguratorOption option, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(option.ValuesJson))
            return ([], null);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(option.ValuesJson);
        }
        catch (JsonException)
        {
            errors.Add($"Option '{option.Name}' has a list of values that is not valid JSON.");
            return ([], null);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
                return ([], root.Deserialize<CpqDefinitions.NumericRange>(CpqDefinitions.Json));
            if (root.ValueKind != JsonValueKind.Array)
                return ([], null);

            var allowed = new List<CpqDefinitions.AllowedValue>();
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    allowed.Add(new CpqDefinitions.AllowedValue(item.GetString()!.Trim()));
                else if (item.ValueKind == JsonValueKind.Number)
                    allowed.Add(new CpqDefinitions.AllowedValue(item.GetRawText(), null, item.GetDecimal()));
                else if (item.ValueKind == JsonValueKind.Object
                         && item.Deserialize<CpqDefinitions.AllowedValue>(CpqDefinitions.Json) is { Value: { Length: > 0 } } value)
                    allowed.Add(value with { Value = value.Value.Trim() });
            }
            return (allowed, null);
        }
    }

    private static CpqDefinitions.PricingRule ParsePricingRule(ConfiguratorOption option, List<string> errors)
    {
        var json = option.PricingRuleJson?.Trim();
        if (string.IsNullOrEmpty(json))
            return new CpqDefinitions.PricingRule(null, null);
        var message = $"Option '{option.Name}' has a pricing rule that is not valid JSON.";
        return json.StartsWith('[')
            ? new CpqDefinitions.PricingRule(Parse<List<CpqDefinitions.PriceAdder>>(json, message, errors), null)
            : Parse<CpqDefinitions.PricingRule>(json, message, errors) ?? new CpqDefinitions.PricingRule(null, null);
    }

    private static IEnumerable<string> MatchingValues(OptionState state, string? when)
    {
        if (!state.HasValue)
            return [];
        if (string.IsNullOrWhiteSpace(when) || when.Trim() == "*")
            return state.Values;
        if (state.Option.OptionType == ConfiguratorOptionType.Checkbox)
            return IsTrue(when) ? state.Values : [];
        return state.Values.Where(v => string.Equals(v, when.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyRule(CpqDefinitions.ValidationRule rule, List<OptionState> states, List<string> errors)
    {
        if (rule.If is null || rule.Then is null)
        {
            errors.Add("A validation rule needs both an 'if' and a 'then' condition.");
            return;
        }

        var kind = rule.Type?.Trim().ToLowerInvariant();
        if (kind is not ("requires" or "excludes" or "when"))
        {
            errors.Add($"'{rule.Type}' is not a validation rule type. Use requires or excludes.");
            return;
        }

        if (!Holds(rule.If, states, errors))
            return;

        var then = Holds(rule.Then, states, errors);
        if (kind == "excludes" ? then : !then)
            errors.Add(rule.Message?.Trim() is { Length: > 0 } message
                ? message
                : kind == "excludes"
                    ? $"{Describe(rule.If)} cannot be combined with {Describe(rule.Then)}."
                    : $"{Describe(rule.If)} requires {Describe(rule.Then)}.");
    }

    private static bool Holds(CpqDefinitions.Condition condition, List<OptionState> states, List<string> errors)
    {
        var state = states.FirstOrDefault(s => string.Equals(s.Option.Name.Trim(), condition.Option?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (state is null)
        {
            errors.Add($"A validation rule refers to '{condition.Option}', which is not an option of this configurator.");
            return false;
        }

        if (condition.Selected is bool selected && selected != state.HasValue)
            return false;
        if (condition.EqualsValue is { } equals && !(state.HasValue && MatchingValues(state, equals).Any()))
            return false;
        if (condition.In is { Count: > 0 } any && !any.Any(v => state.HasValue && MatchingValues(state, v).Any()))
            return false;
        if (condition.Min is decimal min && !(state.HasValue && state.Number >= min))
            return false;
        if (condition.Max is decimal max && !(state.HasValue && state.Number <= max))
            return false;

        return condition.Selected is not null || condition.EqualsValue is not null || condition.In is { Count: > 0 }
               || condition.Min is not null || condition.Max is not null || state.HasValue;
    }

    private static string Describe(CpqDefinitions.Condition condition) =>
        condition.EqualsValue is { } v ? $"{condition.Option} = {v}"
        : condition.In is { Count: > 0 } any ? $"{condition.Option} in ({string.Join(", ", any)})"
        : condition.Selected == false ? $"no {condition.Option}"
        : condition.Option;

    private static decimal EvaluateFormula(
        string? pricingFormulaJson, List<OptionState> states, decimal basePrice, decimal quantity, List<string> errors)
    {
        var expression = FormulaText(pricingFormulaJson, errors);
        if (string.IsNullOrWhiteSpace(expression))
            return 0m;

        var variables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [QuantityVariable] = quantity,
            [BasePriceVariable] = basePrice,
        };
        foreach (var state in states)
        {
            var value = state.HasValue ? state.Number : 0m;
            variables[state.Option.Name.Trim()] = value;
            variables[CpqFormulaEvaluator.IdentifierFor(state.Option.Name)] = value;
        }

        try
        {
            return CpqFormulaEvaluator.Evaluate(expression, variables);
        }
        catch (CpqFormulaException ex)
        {
            errors.Add(ex.Message);
            return 0m;
        }
    }

    private static string? FormulaText(string? pricingFormulaJson, List<string> errors)
    {
        var text = pricingFormulaJson?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (text.StartsWith('{'))
            return Parse<CpqDefinitions.PricingFormula>(text, "The pricing formula is not valid JSON.", errors)?.Expression;
        if (text.StartsWith('"'))
            return Parse<string>(text, "The pricing formula is not valid JSON.", errors);
        return text;
    }

    private static T? Parse<T>(string? json, string message, List<string> errors) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, CpqDefinitions.Json);
        }
        catch (JsonException)
        {
            errors.Add(message);
            return null;
        }
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, CpqDefinitions.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
