using FluentAssertions;

using Forge.Api.Services.Cpq;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Tests.Services.Cpq;

public sealed class CpqEngineTests
{
    private static ProductConfigurator Configurator(string? rules = null, string? formula = null, decimal basePrice = 100m)
    {
        var configurator = new ProductConfigurator
        {
            Id = 1,
            Name = "Bracket",
            BasePartId = 10,
            BasePart = new Part { Id = 10, PartNumber = "BRK-100", Name = "Bracket" },
            BasePrice = basePrice,
            ValidationRulesJson = rules,
            PricingFormulaJson = formula,
        };
        configurator.Options.Add(new ConfiguratorOption
        {
            Id = 1, Name = "Color", OptionType = ConfiguratorOptionType.Select, SortOrder = 1, IsRequired = true,
            ValuesJson = """["Red", "Blue", {"value": "Gold", "number": 3}]""",
            PricingRuleJson = """{"adders": [{"when": "Gold", "type": "PerUnit", "amount": 20}], "leadTimeDays": [{"when": "Gold", "days": 5}]}""",
            BomImpactJson = """[{"when": "Gold", "partId": 77, "quantity": 2}]""",
        });
        configurator.Options.Add(new ConfiguratorOption
        {
            Id = 2, Name = "Coating", OptionType = ConfiguratorOptionType.Checkbox, SortOrder = 2,
            PricingRuleJson = """[{"type": "Fixed", "amount": 50}]""",
            RoutingImpactJson = """[{"title": "Powder coat", "workCenterId": 4, "setupMinutes": 30, "runMinutesEach": 2}]""",
        });
        configurator.Options.Add(new ConfiguratorOption
        {
            Id = 3, Name = "Overall length", OptionType = ConfiguratorOptionType.Numeric, SortOrder = 3,
            ValuesJson = """{"min": 10, "max": 48}""", DefaultValue = "12",
            PricingRuleJson = """[{"type": "PerValue", "amount": 0.5}]""",
        });
        configurator.Options.Add(new ConfiguratorOption
        {
            Id = 4, Name = "Finish", OptionType = ConfiguratorOptionType.Select, SortOrder = 4,
            ValuesJson = """["Matte", "Gloss"]""", DependsOnOptionId = "Coating",
            PricingRuleJson = """[{"when": "Gloss", "type": "Percent", "amount": 10}]""",
        });
        return configurator;
    }

    private static Dictionary<string, string> Pick(params (string Key, string Value)[] picks)
        => picks.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Prices_base_plus_adders_and_spreads_a_fixed_charge_over_the_quantity()
    {
        var result = CpqEngine.Evaluate(
            Configurator(), Pick(("Color", "Gold"), ("Coating", "true"), ("Overall length", "20"), ("Finish", "Gloss")), 10m);

        result.IsValid.Should().BeTrue(string.Join(" ", result.Errors));
        result.UnitPrice.Should().Be(100m + 20m + 5m + 10m + 10m);
        result.ExtendedPrice.Should().Be(1450m);
        result.LeadTimeDays.Should().Be(5);
        result.Components.Should().ContainSingle(c => c.PartId == 77 && c.Quantity == 2m);
        result.RoutingSteps.Should().ContainSingle(s => s.Title == "Powder coat" && s.WorkCenterId == 4);
    }

    [Fact]
    public void A_missing_required_option_and_a_value_off_the_list_are_errors()
    {
        CpqEngine.Evaluate(Configurator(), Pick(), 1m).Errors.Should().Contain("Color is required.");
        CpqEngine.Evaluate(Configurator(), Pick(("Color", "Green")), 1m).Errors.Should().Contain("'Green' is not a choice for Color.");
    }

    [Fact]
    public void Numeric_options_take_their_default_and_are_held_to_their_range()
    {
        CpqEngine.Evaluate(Configurator(), Pick(("Color", "Red")), 1m).UnitPrice.Should().Be(106m);
        CpqEngine.Evaluate(Configurator(), Pick(("Color", "Red"), ("Overall length", "60")), 1m)
            .Errors.Should().Contain("Overall length must be at most 48.");
        CpqEngine.Evaluate(Configurator(), Pick(("Color", "Red"), ("Overall length", "long")), 1m)
            .Errors.Should().Contain("Overall length must be a number.");
    }

    [Fact]
    public void A_dependent_option_is_ignored_while_its_parent_is_off()
    {
        var result = CpqEngine.Evaluate(Configurator(), Pick(("Color", "Red"), ("Finish", "Gloss")), 1m);

        result.IsValid.Should().BeTrue();
        result.UnitPrice.Should().Be(106m);
        result.Selections.Should().NotContainKey("Finish");
    }

    [Fact]
    public void A_requires_rule_fails_when_its_condition_holds_and_the_requirement_does_not()
    {
        const string rules = """[{"type": "requires", "if": {"option": "Color", "equals": "Gold"}, "then": {"option": "Coating", "selected": true}}]""";

        CpqEngine.Evaluate(Configurator(rules), Pick(("Color", "Gold")), 1m)
            .Errors.Should().ContainSingle().Which.Should().Be("Color = Gold requires Coating.");
        CpqEngine.Evaluate(Configurator(rules), Pick(("Color", "Gold"), ("Coating", "true")), 1m).IsValid.Should().BeTrue();
        CpqEngine.Evaluate(Configurator(rules), Pick(("Color", "Red")), 1m).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_excludes_rule_fails_when_both_sides_hold_and_uses_its_own_message()
    {
        const string rules = """[{"type": "excludes", "if": {"option": "Color", "in": ["Red", "Blue"]}, "then": {"option": "Overall length", "min": 40}, "message": "Painted brackets stop at 40 in."}]""";

        CpqEngine.Evaluate(Configurator(rules), Pick(("Color", "Blue"), ("Overall length", "44")), 1m)
            .Errors.Should().ContainSingle().Which.Should().Be("Painted brackets stop at 40 in.");
        CpqEngine.Evaluate(Configurator(rules), Pick(("Color", "Blue"), ("Overall length", "30")), 1m).IsValid.Should().BeTrue();
        CpqEngine.Evaluate(Configurator(rules), Pick(("Color", "Gold"), ("Overall length", "44")), 1m).IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_pricing_formula_adds_per_unit_over_option_values_and_quantity()
    {
        const string formula = """{"expression": "if([Overall length] > 24, ([Overall length] - 24) * 1.25, 0) + Color * 2 - if(quantity >= 100, 5, 0)"}""";

        var result = CpqEngine.Evaluate(Configurator(formula: formula), Pick(("Color", "Gold"), ("Overall length", "30")), 100m);

        result.IsValid.Should().BeTrue(string.Join(" ", result.Errors));
        result.UnitPrice.Should().Be(100m + 20m + 15m + 7.5m + 6m - 5m);
    }

    [Fact]
    public void A_broken_formula_or_rule_json_is_an_error_not_an_exception()
    {
        CpqEngine.Evaluate(Configurator(formula: "Color *"), Pick(("Color", "Red")), 1m)
            .Errors.Should().ContainSingle().Which.Should().Contain("ends too early");
        CpqEngine.Evaluate(Configurator(formula: "Weight * 2"), Pick(("Color", "Red")), 1m)
            .Errors.Should().ContainSingle().Which.Should().Contain("'Weight'");
        CpqEngine.Evaluate(Configurator(rules: "{not json"), Pick(("Color", "Red")), 1m)
            .Errors.Should().Contain("The configurator's validation rules are not valid JSON.");
    }

    [Fact]
    public void Quantity_must_be_positive()
        => CpqEngine.Evaluate(Configurator(), Pick(("Color", "Red")), 0m).Errors.Should().Contain("Quantity must be greater than zero.");

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("-4 + 10 / 4", -1.5)]
    [InlineData("max(1, 7, 3) + min(2, 5)", 9)]
    [InlineData("round(2.345, 2) + ceil(0.2) + floor(1.9) + abs(-1)", 5.35)]
    [InlineData("10 % 4 == 2 && !(1 > 2)", 1)]
    [InlineData("if(a >= 3 || b == 0, a * b, 0)", 0)]
    public void The_formula_evaluator_follows_arithmetic_precedence(string expression, double expected)
        => CpqFormulaEvaluator.Evaluate(expression, new Dictionary<string, decimal> { ["a"] = 3m, ["b"] = 0m })
            .Should().Be((decimal)expected);

    [Fact]
    public void The_formula_evaluator_refuses_division_by_zero_and_unknown_functions()
    {
        var empty = new Dictionary<string, decimal>();
        FluentActions.Invoking(() => CpqFormulaEvaluator.Evaluate("1 / 0", empty))
            .Should().Throw<CpqFormulaException>().WithMessage("*divides by zero*");
        FluentActions.Invoking(() => CpqFormulaEvaluator.Evaluate("system(1)", empty))
            .Should().Throw<CpqFormulaException>().WithMessage("*not a pricing formula function*");
        FluentActions.Invoking(() => CpqFormulaEvaluator.Evaluate("79228162514264337593543950335 * 2", empty))
            .Should().Throw<CpqFormulaException>().WithMessage("*too large*");
    }
}
