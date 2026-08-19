using System.Text.Json;

namespace Forge.Api.Features.Sequences.GateSources;

/// <summary>Tiny reader over a gate's <c>config_json</c> so sources don't each re-implement JsonDocument plumbing.</summary>
public sealed class SequenceGateConfig
{
    private readonly JsonElement _root;

    private SequenceGateConfig(JsonElement root) => _root = root;

    public static SequenceGateConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SequenceGateConfig(default);
        using var doc = JsonDocument.Parse(json);
        return new SequenceGateConfig(doc.RootElement.Clone());
    }

    public string? GetString(string name) =>
        _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public int? GetInt(string name) =>
        _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public bool GetBool(string name) =>
        _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public DateTimeOffset? GetDate(string name) =>
        DateTimeOffset.TryParse(GetString(name), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
