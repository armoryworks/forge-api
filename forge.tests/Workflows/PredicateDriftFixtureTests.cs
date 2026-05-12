using System.Text.Json;

using FluentAssertions;

using Forge.Api.Workflows;

namespace Forge.Tests.Workflows;

/// <summary>
/// Workflow Pattern Phase 4 — Cross-tier drift test for the predicate
/// evaluator. Loads the same JSON fixture file the TS spec consumes
/// (forge-ui/src/app/shared/services/__fixtures__/predicate-drift-fixtures.json,
/// mirrored to forge-tests/Workflows/PredicateDriftFixtures.json so
/// the test project doesn't need a path back across repos) and asserts
/// the C# evaluator returns the same boolean for every (predicate, entity).
///
/// If both this and <c>predicate-drift-fixtures.spec.ts</c> pass, the two
/// implementations are in lock-step on the documented inputs. Adding a
/// fixture case is a CONTRACT change: it MUST run green in BOTH suites
/// before landing.
/// </summary>
public class PredicateDriftFixtureTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Workflows", "PredicateDriftFixtures.json");

    private readonly PredicateEvaluator _evaluator = new();

    public static IEnumerable<object[]> Cases()
    {
        var json = File.ReadAllText(FixturePath);
        using var doc = JsonDocument.Parse(json);
        var cases = doc.RootElement.GetProperty("cases");
        foreach (var c in cases.EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            // Re-serialize so xUnit can hold the JsonElements safely (they
            // can't outlive the JsonDocument they came from).
            var predicateJson = c.GetProperty("predicate").GetRawText();
            var entityJson = c.GetProperty("entity").GetRawText();
            var expected = c.GetProperty("expected").GetBoolean();
            yield return [name, predicateJson, entityJson, expected];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Fixture_TsAndCsharp_Agree(string name, string predicateJson, string entityJson, bool expected)
    {
        // Parse the entity into a Dictionary<string, object?> tree so the
        // reflection-based evaluator can walk it with the same field names
        // the TS evaluator uses (camelCase). The C# evaluator title-cases
        // before reflecting, so the same camelCase keys work on both tiers.
        var entity = JsonToObject(JsonDocument.Parse(entityJson).RootElement);

        using var pdoc = JsonDocument.Parse(predicateJson);
        var actual = _evaluator.Evaluate(pdoc.RootElement, entity);

        actual.Should().Be(expected, $"fixture case '{name}' failed drift check");
    }

    /// <summary>
    /// Convert a JsonElement into a plain object graph (Dictionary / List /
    /// scalar) so the property-reflection path in PredicateEvaluator can
    /// walk it. We preserve camelCase keys to match the TS-side surface.
    /// </summary>
    private static object? JsonToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => el.EnumerateObject()
            .ToDictionary(p => p.Name, p => JsonToObject(p.Value)),
        JsonValueKind.Array => el.EnumerateArray().Select(JsonToObject).ToList(),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => null,
    };
}
