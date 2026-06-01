using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 / D6 — Shared interpreter for the entity-readiness
/// predicate DSL. Operators v1:
///   • <c>fieldPresent</c>             — value not null (and, for strings, non-empty)
///   • <c>fieldEquals</c>              — strict equality
///   • <c>fieldCompare</c>             — gt / lt / gte / lte / ne
///   • <c>relationExists</c>           — collection size ≥ minCount
///   • <c>relationCountCompare</c>     — collection size compared by op + value
///   • <c>all</c> / <c>any</c> / <c>not</c> — boolean composition
///   • <c>custom</c>                   — registry lookup (v1 returns false-with-warning)
///
/// PropertyName casing: predicate JSON should use camelCase to match the
/// frontend twin; the evaluator title-cases the first character before
/// reflecting against the C# entity (so "name" → "Name", "bomLines" →
/// "BomLines"). Unknown fields / unknown operators short-circuit to
/// <c>false</c> and emit a single warning per evaluation.
///
/// This is intentionally NOT a full JSONLogic — the operator surface is
/// fixed in v1 and the registry hook lets us add complex predicates as
/// named C# functions without expanding the DSL footprint.
/// </summary>
public class PredicateEvaluator(ILogger<PredicateEvaluator>? logger = null,
                                IPredicateCustomFunctionRegistry? customRegistry = null)
{
    private readonly ILogger<PredicateEvaluator>? _logger = logger;
    private readonly IPredicateCustomFunctionRegistry _customRegistry =
        customRegistry ?? new EmptyPredicateCustomFunctionRegistry();

    /// <summary>
    /// Convenience overload that parses a JSON string predicate before
    /// evaluating. Returns false on malformed JSON (with a logged warning).
    /// </summary>
    public bool Evaluate(string predicateJson, object? entity)
    {
        if (string.IsNullOrWhiteSpace(predicateJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(predicateJson);
            return Evaluate(doc.RootElement, entity);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "[WORKFLOW-PREDICATE] Malformed predicate JSON");
            return false;
        }
    }

    public bool Evaluate(JsonElement predicate, object? entity)
    {
        if (entity is null) return false;
        if (predicate.ValueKind != JsonValueKind.Object) return false;
        if (!predicate.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
            return false;

        var type = typeProp.GetString();
        return type switch
        {
            "fieldPresent" => EvaluateFieldPresent(predicate, entity),
            "fieldEquals" => EvaluateFieldEquals(predicate, entity),
            "fieldCompare" => EvaluateFieldCompare(predicate, entity),
            "relationExists" => EvaluateRelationExists(predicate, entity),
            "relationCountCompare" => EvaluateRelationCountCompare(predicate, entity),
            "all" => EvaluateAll(predicate, entity),
            "any" => EvaluateAny(predicate, entity),
            "not" => EvaluateNot(predicate, entity),
            "custom" => EvaluateCustom(predicate, entity),
            _ => Warn($"Unknown predicate type '{type}'"),
        };
    }

    private bool EvaluateFieldPresent(JsonElement predicate, object entity)
    {
        if (!TryReadField(predicate, entity, out var value)) return false;
        return IsPresent(value);
    }

    private bool EvaluateFieldEquals(JsonElement predicate, object entity)
    {
        if (!TryReadField(predicate, entity, out var value)) return false;
        if (!predicate.TryGetProperty("value", out var expected)) return false;
        return AreEqual(value, expected);
    }

    private bool EvaluateFieldCompare(JsonElement predicate, object entity)
    {
        if (!TryReadField(predicate, entity, out var value)) return false;
        if (!predicate.TryGetProperty("op", out var opProp) || opProp.ValueKind != JsonValueKind.String) return false;
        if (!predicate.TryGetProperty("value", out var expected)) return false;
        return Compare(value, expected, opProp.GetString()!);
    }

    private bool EvaluateRelationExists(JsonElement predicate, object entity)
    {
        if (!predicate.TryGetProperty("relation", out var relProp) || relProp.ValueKind != JsonValueKind.String)
            return false;

        var minCount = 1;
        if (predicate.TryGetProperty("minCount", out var mcProp) && mcProp.TryGetInt32(out var mc))
            minCount = mc;

        var count = ReadRelationCount(entity, relProp.GetString()!);
        return count.HasValue && count.Value >= minCount;
    }

    private bool EvaluateRelationCountCompare(JsonElement predicate, object entity)
    {
        if (!predicate.TryGetProperty("relation", out var relProp) || relProp.ValueKind != JsonValueKind.String)
            return false;
        if (!predicate.TryGetProperty("op", out var opProp) || opProp.ValueKind != JsonValueKind.String) return false;
        if (!predicate.TryGetProperty("value", out var valueProp) || !valueProp.TryGetInt32(out var expected))
            return false;

        var count = ReadRelationCount(entity, relProp.GetString()!);
        if (!count.HasValue) return false;

        return CompareNumeric(count.Value, expected, opProp.GetString()!);
    }

    private bool EvaluateAll(JsonElement predicate, object entity)
    {
        if (!predicate.TryGetProperty("of", out var ofProp) || ofProp.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var child in ofProp.EnumerateArray())
            if (!Evaluate(child, entity)) return false;
        return true;
    }

    private bool EvaluateAny(JsonElement predicate, object entity)
    {
        if (!predicate.TryGetProperty("of", out var ofProp) || ofProp.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var child in ofProp.EnumerateArray())
            if (Evaluate(child, entity)) return true;
        return false;
    }

    private bool EvaluateNot(JsonElement predicate, object entity)
    {
        if (!predicate.TryGetProperty("of", out var child)) return false;
        return !Evaluate(child, entity);
    }

    private bool EvaluateCustom(JsonElement predicate, object entity)
    {
        if (!predicate.TryGetProperty("ref", out var refProp) || refProp.ValueKind != JsonValueKind.String)
            return false;
        var key = refProp.GetString()!;
        if (_customRegistry.TryGet(key, out var fn))
        {
            try
            {
                return fn(predicate, entity);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[WORKFLOW-PREDICATE] Custom predicate '{Key}' threw", key);
                return false;
            }
        }
        return Warn($"Custom predicate '{key}' not registered");
    }

    // ─── Helpers ───

    private bool TryReadField(JsonElement predicate, object entity, out object? value)
    {
        value = null;
        if (!predicate.TryGetProperty("field", out var fieldProp) || fieldProp.ValueKind != JsonValueKind.String)
            return false;
        var fieldName = fieldProp.GetString()!;
        return TryGetMemberValue(entity, fieldName, out value);
    }

    private static bool TryGetMemberValue(object entity, string fieldName, out object? value)
    {
        value = null;
        if (string.IsNullOrEmpty(fieldName)) return false;

        // Dictionary-shaped entities (used by the cross-tier drift test, where
        // JSON is hydrated into Dictionary<string, object?> instead of a POCO)
        // — accept both camelCase exact match and PascalCase fallback.
        if (entity is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue(fieldName, out var v))
            {
                value = v;
                return true;
            }
            var pascalKey = char.ToUpperInvariant(fieldName[0]) + fieldName[1..];
            if (dict.TryGetValue(pascalKey, out v))
            {
                value = v;
                return true;
            }
            return false;
        }

        var pascal = char.ToUpperInvariant(fieldName[0]) + fieldName[1..];
        var type = entity.GetType();

        // Try property first (most entity members are properties).
        var prop = type.GetProperty(pascal,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop is not null)
        {
            value = prop.GetValue(entity);
            return true;
        }

        var field = type.GetField(pascal,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (field is not null)
        {
            value = field.GetValue(entity);
            return true;
        }

        return false;
    }

    private static int? ReadRelationCount(object entity, string relationName)
    {
        if (!TryGetMemberValue(entity, relationName, out var rel) || rel is null)
            return 0; // Missing relation treated as zero — graceful for partially-loaded entities.

        if (rel is ICollection collection) return collection.Count;
        if (rel is IEnumerable enumerable)
        {
            var n = 0;
            foreach (var _ in enumerable) n++;
            return n;
        }
        return null;
    }

    private static bool IsPresent(object? value)
    {
        if (value is null) return false;
        if (value is string s) return !string.IsNullOrWhiteSpace(s);
        return true;
    }

    private static bool AreEqual(object? actual, JsonElement expected)
    {
        if (actual is null) return expected.ValueKind is JsonValueKind.Null;
        return expected.ValueKind switch
        {
            JsonValueKind.Null => actual is null,
            JsonValueKind.True => actual is bool ab && ab,
            JsonValueKind.False => actual is bool ab && !ab,
            JsonValueKind.String => string.Equals(actual.ToString(), expected.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => CompareNumericRaw(actual, expected, "eq"),
            _ => false,
        };
    }

    private static bool Compare(object? actual, JsonElement expected, string op)
    {
        if (op == "eq") return AreEqual(actual, expected);
        if (op == "ne") return !AreEqual(actual, expected);

        // For gt/lt/gte/lte we need numeric or comparable values.
        return CompareNumericRaw(actual, expected, op);
    }

    private static bool CompareNumericRaw(object? actual, JsonElement expected, string op)
    {
        if (actual is null) return false;
        if (!TryToDouble(actual, out var a)) return false;
        if (!expected.TryGetDouble(out var b)) return false;
        return CompareNumeric(a, b, op);
    }

    private static bool CompareNumeric(double a, double b, string op) => op switch
    {
        "eq" => a == b,
        "ne" => a != b,
        "gt" => a > b,
        "lt" => a < b,
        "gte" => a >= b,
        "lte" => a <= b,
        _ => false,
    };

    private static bool TryToDouble(object value, out double result)
    {
        result = 0;
        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case decimal dec: result = (double)dec; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case short sh: result = sh; return true;
            case byte by: result = by; return true;
            case bool b: result = b ? 1 : 0; return true;
            case string s when double.TryParse(s, System.Globalization.NumberStyles.Any,
                                               System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                result = parsed; return true;
            default: return false;
        }
    }

    private bool Warn(string message)
    {
        _logger?.LogWarning("[WORKFLOW-PREDICATE] {Message}", message);
        return false;
    }
}

/// <summary>
/// Registry stub for v1 — empty implementation returns false-with-warning when
/// a <c>custom</c> predicate is encountered. Future phases plug a real registry
/// here without changing the evaluator surface.
/// </summary>
public interface IPredicateCustomFunctionRegistry
{
    bool TryGet(string key, out Func<JsonElement, object, bool> fn);
}

public sealed class EmptyPredicateCustomFunctionRegistry : IPredicateCustomFunctionRegistry
{
    public bool TryGet(string key, out Func<JsonElement, object, bool> fn)
    {
        fn = (_, _) => false;
        return false;
    }
}
