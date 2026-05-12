using System.Text.Json;

using FluentAssertions;

using Forge.Api.Workflows;

namespace Forge.Tests.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Unit tests for the shared predicate evaluator.
/// Covers every operator on representative inputs plus the documented
/// "graceful false" behavior for missing fields and unknown types.
/// </summary>
public class PredicateEvaluatorTests
{
    private readonly PredicateEvaluator _evaluator = new();

    private sealed class FakeEntity
    {
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public decimal? ManualCostOverride { get; set; }
        public int? CurrentCostCalculationId { get; set; }
        public bool IsActive { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<FakeChild> Children { get; set; } = [];
    }

    private sealed class FakeChild
    {
        public int Id { get; set; }
    }

    private static JsonElement P(string json)
        => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void FieldPresent_NonEmptyString_True()
    {
        var entity = new FakeEntity { Name = "Widget" };
        _evaluator.Evaluate(P("""{"type":"fieldPresent","field":"name"}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldPresent_NullField_False()
    {
        var entity = new FakeEntity();
        _evaluator.Evaluate(P("""{"type":"fieldPresent","field":"name"}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void FieldPresent_EmptyString_False()
    {
        var entity = new FakeEntity { Name = "" };
        _evaluator.Evaluate(P("""{"type":"fieldPresent","field":"name"}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void FieldPresent_WhitespaceString_False()
    {
        var entity = new FakeEntity { Name = "   " };
        _evaluator.Evaluate(P("""{"type":"fieldPresent","field":"name"}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void FieldPresent_NullableInt_True_When_Set()
    {
        var entity = new FakeEntity { CurrentCostCalculationId = 42 };
        _evaluator.Evaluate(P("""{"type":"fieldPresent","field":"currentCostCalculationId"}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldPresent_NullableDecimal_True_When_Set()
    {
        var entity = new FakeEntity { ManualCostOverride = 12.50m };
        _evaluator.Evaluate(P("""{"type":"fieldPresent","field":"manualCostOverride"}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldPresent_UnknownField_False_NotThrow()
    {
        var entity = new FakeEntity();
        _evaluator.Evaluate(P("""{"type":"fieldPresent","field":"nonexistent"}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void FieldEquals_StringMatch_True()
    {
        var entity = new FakeEntity { Name = "ASM-100" };
        _evaluator.Evaluate(P("""{"type":"fieldEquals","field":"name","value":"ASM-100"}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldEquals_StringMismatch_False()
    {
        var entity = new FakeEntity { Name = "ASM-100" };
        _evaluator.Evaluate(P("""{"type":"fieldEquals","field":"name","value":"PART-1"}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void FieldEquals_BoolMatch_True()
    {
        var entity = new FakeEntity { IsActive = true };
        _evaluator.Evaluate(P("""{"type":"fieldEquals","field":"isActive","value":true}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldEquals_NumericMatch_True()
    {
        var entity = new FakeEntity { Quantity = 5 };
        _evaluator.Evaluate(P("""{"type":"fieldEquals","field":"quantity","value":5}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldCompare_Gt_True()
    {
        var entity = new FakeEntity { Quantity = 10 };
        _evaluator.Evaluate(P("""{"type":"fieldCompare","field":"quantity","op":"gt","value":5}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldCompare_Lt_True()
    {
        var entity = new FakeEntity { Quantity = 1 };
        _evaluator.Evaluate(P("""{"type":"fieldCompare","field":"quantity","op":"lt","value":5}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldCompare_Gte_BoundaryTrue()
    {
        var entity = new FakeEntity { Quantity = 5 };
        _evaluator.Evaluate(P("""{"type":"fieldCompare","field":"quantity","op":"gte","value":5}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldCompare_Lte_BoundaryTrue()
    {
        var entity = new FakeEntity { Quantity = 5 };
        _evaluator.Evaluate(P("""{"type":"fieldCompare","field":"quantity","op":"lte","value":5}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldCompare_Ne_True()
    {
        var entity = new FakeEntity { Quantity = 5 };
        _evaluator.Evaluate(P("""{"type":"fieldCompare","field":"quantity","op":"ne","value":7}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void FieldCompare_DecimalNumeric_True()
    {
        var entity = new FakeEntity { ManualCostOverride = 12.5m };
        _evaluator.Evaluate(P("""{"type":"fieldCompare","field":"manualCostOverride","op":"gt","value":10}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void RelationExists_DefaultMinCount_True()
    {
        var entity = new FakeEntity { Children = [new FakeChild { Id = 1 }] };
        _evaluator.Evaluate(P("""{"type":"relationExists","relation":"children"}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void RelationExists_EmptyCollection_False()
    {
        var entity = new FakeEntity();
        _evaluator.Evaluate(P("""{"type":"relationExists","relation":"children"}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void RelationExists_MinCount_NotMet_False()
    {
        var entity = new FakeEntity { Children = [new FakeChild { Id = 1 }] };
        _evaluator.Evaluate(P("""{"type":"relationExists","relation":"children","minCount":2}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void RelationExists_MinCount_Met_True()
    {
        var entity = new FakeEntity { Children = [new FakeChild(), new FakeChild()] };
        _evaluator.Evaluate(P("""{"type":"relationExists","relation":"children","minCount":2}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void RelationCountCompare_Gt_True()
    {
        var entity = new FakeEntity { Tags = ["a", "b", "c"] };
        _evaluator.Evaluate(P("""{"type":"relationCountCompare","relation":"tags","op":"gt","value":2}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void RelationCountCompare_Eq_True()
    {
        var entity = new FakeEntity { Tags = ["a", "b"] };
        _evaluator.Evaluate(P("""{"type":"relationCountCompare","relation":"tags","op":"eq","value":2}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void All_AllChildrenTrue_True()
    {
        var entity = new FakeEntity { Name = "X", Quantity = 5 };
        var pred = """
        {"type":"all","of":[
          {"type":"fieldPresent","field":"name"},
          {"type":"fieldCompare","field":"quantity","op":"gte","value":1}
        ]}
        """;
        _evaluator.Evaluate(P(pred), entity).Should().BeTrue();
    }

    [Fact]
    public void All_OneChildFalse_False()
    {
        var entity = new FakeEntity { Name = "X", Quantity = 0 };
        var pred = """
        {"type":"all","of":[
          {"type":"fieldPresent","field":"name"},
          {"type":"fieldCompare","field":"quantity","op":"gte","value":1}
        ]}
        """;
        _evaluator.Evaluate(P(pred), entity).Should().BeFalse();
    }

    [Fact]
    public void Any_OneChildTrue_True()
    {
        var entity = new FakeEntity { ManualCostOverride = 5m };
        var pred = """
        {"type":"any","of":[
          {"type":"fieldPresent","field":"manualCostOverride"},
          {"type":"fieldPresent","field":"currentCostCalculationId"}
        ]}
        """;
        _evaluator.Evaluate(P(pred), entity).Should().BeTrue();
    }

    [Fact]
    public void Any_AllChildrenFalse_False()
    {
        var entity = new FakeEntity();
        var pred = """
        {"type":"any","of":[
          {"type":"fieldPresent","field":"manualCostOverride"},
          {"type":"fieldPresent","field":"currentCostCalculationId"}
        ]}
        """;
        _evaluator.Evaluate(P(pred), entity).Should().BeFalse();
    }

    [Fact]
    public void Not_InvertsChild()
    {
        var entity = new FakeEntity { Name = "Widget" };
        _evaluator.Evaluate(P("""{"type":"not","of":{"type":"fieldPresent","field":"name"}}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void Not_OnFalseChild_True()
    {
        var entity = new FakeEntity();
        _evaluator.Evaluate(P("""{"type":"not","of":{"type":"fieldPresent","field":"name"}}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void Custom_UnknownRef_False()
    {
        // Unknown registry key returns false-with-warning; doesn't throw.
        var entity = new FakeEntity();
        _evaluator.Evaluate(P("""{"type":"custom","ref":"someComplexRule"}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void UnknownPredicateType_False_NotThrow()
    {
        var entity = new FakeEntity();
        _evaluator.Evaluate(P("""{"type":"madeUp","field":"name"}"""), entity).Should().BeFalse();
    }

    [Fact]
    public void NullEntity_False()
    {
        _evaluator.Evaluate(P("""{"type":"fieldPresent","field":"name"}"""), null).Should().BeFalse();
    }

    [Fact]
    public void MalformedJson_False_NotThrow()
    {
        var entity = new FakeEntity();
        _evaluator.Evaluate("not valid json {{{", entity).Should().BeFalse();
    }

    [Fact]
    public void NestedAllInsideAny_RegressionCheck()
    {
        // Mirrors the Part 'hasCost' shape from the worked example.
        var entity = new FakeEntity { Name = "X", Quantity = 5, ManualCostOverride = 9m };
        var pred = """
        {"type":"all","of":[
          {"type":"fieldPresent","field":"name"},
          {"type":"any","of":[
            {"type":"fieldPresent","field":"manualCostOverride"},
            {"type":"fieldPresent","field":"currentCostCalculationId"}
          ]}
        ]}
        """;
        _evaluator.Evaluate(P(pred), entity).Should().BeTrue();
    }

    [Fact]
    public void CustomRegistry_Hit_DelegatesToFunction()
    {
        var registry = new TestRegistry();
        registry.Register("alwaysTrue", (_, _) => true);
        var evaluator = new PredicateEvaluator(customRegistry: registry);
        var entity = new FakeEntity();
        evaluator.Evaluate(P("""{"type":"custom","ref":"alwaysTrue"}"""), entity).Should().BeTrue();
    }

    [Fact]
    public void CustomRegistry_FunctionThrows_ReturnsFalse()
    {
        var registry = new TestRegistry();
        registry.Register("explodes", (_, _) => throw new InvalidOperationException("boom"));
        var evaluator = new PredicateEvaluator(customRegistry: registry);
        var entity = new FakeEntity();
        evaluator.Evaluate(P("""{"type":"custom","ref":"explodes"}"""), entity).Should().BeFalse();
    }

    private sealed class TestRegistry : IPredicateCustomFunctionRegistry
    {
        private readonly Dictionary<string, Func<JsonElement, object, bool>> _funcs = new();
        public void Register(string key, Func<JsonElement, object, bool> fn) => _funcs[key] = fn;
        public bool TryGet(string key, out Func<JsonElement, object, bool> fn)
        {
            return _funcs.TryGetValue(key, out fn!);
        }
    }
}
