namespace Forge.Core.Entities;

/// <summary>
/// A configure-to-order product: a base part, a base price and options, evaluated by the CPQ engine
/// (<c>Forge.Api.Services.Cpq.CpqEngine</c>). The admin-authored JSON fields:
/// <list type="bullet">
///   <item><see cref="ValidationRulesJson"/>: an array of rules
///         <c>{ "type": "requires" | "excludes", "if": condition, "then": condition, "message"?: text }</c>.
///         "requires" fails when <c>if</c> holds and <c>then</c> does not; "excludes" fails when both hold. A
///         condition is <c>{ "option": name }</c> (the option has a value) plus at most one of
///         <c>"equals": value</c>, <c>"in": [values]</c>, <c>"selected": true | false</c>, <c>"min"</c> / <c>"max"</c>
///         (numeric options).</item>
///   <item><see cref="PricingFormulaJson"/>: <c>{ "expression": "..." }</c> (or the bare expression), an amount
///         per unit added to the price. Variables are the options by name (numeric value; a checkbox is 1 or 0, a
///         choice its numeric "number" or its value read as a number, a multi-select the count picked),
///         <c>quantity</c> and <c>basePrice</c>; see <c>CpqFormulaEvaluator</c> for the operators and functions.</item>
///   <item><see cref="ConfiguratorOption.ValuesJson"/>: the allowed choices, <c>["Red", "Blue"]</c> or
///         <c>[{ "value": "Red", "label"?: text, "number"?: 2 }]</c>, or <c>{ "min"?: n, "max"?: n }</c> for a numeric
///         option. An empty list allows any value.</item>
///   <item><see cref="ConfiguratorOption.PricingRuleJson"/>: <c>{ "adders": [{ "when"?: value, "type": "Fixed" |
///         "PerUnit" | "Percent" | "PerValue", "amount": n }], "leadTimeDays": [{ "when"?: value, "days": n }] }</c>
///         (or the adders array alone). Fixed is a one-time charge spread over the quantity, PerUnit is per unit,
///         Percent is a percentage of the base price and PerValue multiplies by the option's numeric value. No
///         <c>when</c> applies whenever the option has a value; a multi-select applies it once per value picked.</item>
///   <item><see cref="ConfiguratorOption.BomImpactJson"/>: <c>[{ "when"?: value, "partId" | "partNumber",
///         "quantity": n, "perValue"?: true }]</c>, components the option adds per configured unit.</item>
///   <item><see cref="ConfiguratorOption.RoutingImpactJson"/>: <c>[{ "when"?: value, "title": text,
///         "workCenterId"?: id, "setupMinutes"?: n, "runMinutesEach"?: n }]</c>, steps added after the base routing.</item>
///   <item><see cref="ConfiguratorOption.DependsOnOptionId"/>: <c>"Option"</c> or <c>"Option=Value"</c> (name or id);
///         the option only applies while that holds.</item>
/// </list>
/// </summary>
public class ProductConfigurator : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int BasePartId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ValidationRulesJson { get; set; }
    public decimal? BasePrice { get; set; }
    public string? PricingFormulaJson { get; set; }

    public Part BasePart { get; set; } = null!;
    public ICollection<ConfiguratorOption> Options { get; set; } = [];
    public ICollection<ProductConfiguration> Configurations { get; set; } = [];
}
