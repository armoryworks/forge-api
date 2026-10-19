using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;

namespace Forge.Tests.Architecture;

/// <summary>
/// The browser's capability interceptor (forge-ui <c>capability-endpoint-registry.ts</c>) blocks a
/// request when the capability it maps the URL to is off. It maps by controller, so an action whose
/// <c>[RequiresCapability]</c> override is not a dependent of the controller's capability would be
/// blocked in the browser while the server allows it (the middleware takes the action-level
/// attribute). Every such widening override is listed in <c>capability-action-overrides.json</c>:
/// under <c>mirrored</c> when the UI registry carries a specific entry for it (the forge-ui twin of
/// the file is asserted against the registry), or under <c>notMirrored</c> with the reason it needs
/// none. A new widening override fails this test until it is listed, and both copies of the file
/// change in the same pair of commits (<c>FORGE_STANDARDS_UPDATE_BASELINE=1</c> rewrites
/// <c>mirrored</c> here).
/// </summary>
public sealed class CapabilityActionOverrideTests
{
    private const string ApiPrefix = "api/v1/";

    private static readonly string FixturePath =
        Path.Combine(RepoRoot.Path, "forge.tests", "Architecture", "capability-action-overrides.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>One widening override, keyed by the literal route prefix the UI registry matches on.</summary>
    public sealed record OverrideEntry(string Prefix, string Capability, string? Reason = null);

    /// <summary>The checked-in list, shared with forge-ui.</summary>
    public sealed record OverrideFixture(List<OverrideEntry> Mirrored, List<OverrideEntry> NotMirrored);

    private static bool DependsOn(string capability, string ancestor)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>([capability]);
        while (frontier.Count > 0)
        {
            var code = frontier.Dequeue();
            foreach (var edge in CapabilityCatalogRelations.Dependencies)
            {
                if (edge.From != code) continue;
                if (edge.To == ancestor) return true;
                if (seen.Add(edge.To)) frontier.Enqueue(edge.To);
            }
        }
        return false;
    }

    private static string? LiteralPrefix(string? classTemplate, string? actionTemplate)
    {
        string full;
        if (actionTemplate is not null && (actionTemplate.StartsWith('/') || actionTemplate.StartsWith("~/", StringComparison.Ordinal)))
            full = actionTemplate.TrimStart('~').TrimStart('/');
        else
            full = string.Join('/', new[] { classTemplate, actionTemplate }.Where(t => !string.IsNullOrEmpty(t)));

        if (!full.StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var literal = full[ApiPrefix.Length..].Split('/').TakeWhile(s => !s.Contains('{')).ToList();
        return literal.Count == 0 ? null : string.Join('/', literal).ToLowerInvariant();
    }

    private static List<(string Prefix, string Capability, bool Widening)> GatedActions()
    {
        var found = new HashSet<(string Prefix, string Capability, bool Widening)>();
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var classGate = controller.GetCustomAttribute<RequiresCapabilityAttribute>(inherit: true)?.Capability;
            if (classGate is null) continue;
            var classTemplate = controller.GetCustomAttribute<RouteAttribute>(inherit: true)?.Template;

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (action.GetCustomAttribute<CapabilityBootstrapAttribute>(inherit: true) is not null) continue;
                var actionGate = action.GetCustomAttribute<RequiresCapabilityAttribute>(inherit: true)?.Capability ?? classGate;
                var widening = actionGate != classGate && !DependsOn(actionGate, classGate);

                var templates = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Select(h => h.Template)
                    .Concat(action.GetCustomAttributes<RouteAttribute>(inherit: true).Select(r => r.Template));
                foreach (var template in templates)
                {
                    var prefix = LiteralPrefix(classTemplate, template);
                    if (prefix is not null) found.Add((prefix, actionGate, widening));
                }
            }
        }

        return [.. found];
    }

    private static List<OverrideEntry> WideningOverrides() =>
        GatedActions()
            .Where(a => a.Widening)
            .Select(a => (a.Prefix, a.Capability))
            .Distinct()
            .OrderBy(f => f.Prefix, StringComparer.Ordinal)
            .ThenBy(f => f.Capability, StringComparer.Ordinal)
            .Select(f => new OverrideEntry(f.Prefix, f.Capability))
            .ToList();

    private static OverrideFixture ReadFixture() =>
        File.Exists(FixturePath)
            ? JsonSerializer.Deserialize<OverrideFixture>(File.ReadAllText(FixturePath), JsonOptions) ?? new([], [])
            : new([], []);

    private static string Key(OverrideEntry e) => $"{e.Prefix} -> {e.Capability}";

    [Fact]
    public void Every_widening_action_override_is_listed_for_the_ui_registry()
    {
        var computed = WideningOverrides();
        var fixture = ReadFixture();

        if (Environment.GetEnvironmentVariable(StandardsBaseline.UpdateEnvVar) == "1")
        {
            var exempt = fixture.NotMirrored.Select(Key).ToHashSet(StringComparer.Ordinal);
            var computedKeys = computed.Select(Key).ToHashSet(StringComparer.Ordinal);
            var rewritten = new OverrideFixture(
                computed.Where(e => !exempt.Contains(Key(e))).ToList(),
                fixture.NotMirrored.Where(e => computedKeys.Contains(Key(e))).ToList());
            File.WriteAllText(FixturePath, JsonSerializer.Serialize(rewritten, JsonOptions) + "\n");
            return;
        }

        var listed = fixture.Mirrored.Concat(fixture.NotMirrored).Select(Key).ToList();
        var actual = computed.Select(Key).ToList();

        actual.Except(listed).Should().BeEmpty(
            "these action-level [RequiresCapability] overrides do not depend on their controller's " +
            "capability, so the UI interceptor would block them in the browser. Add a specific entry " +
            "to forge-ui capability-endpoint-registry.ts and list it under 'mirrored' in both copies of " +
            "capability-action-overrides.json (or under 'notMirrored' with a reason)");
        listed.Except(actual).Should().BeEmpty(
            "these listed overrides no longer exist; remove them from both copies of capability-action-overrides.json");
        fixture.NotMirrored.Where(e => string.IsNullOrWhiteSpace(e.Reason)).Select(Key).Should().BeEmpty(
            "every notMirrored entry needs a reason");
    }

    [Fact]
    public void Mirrored_prefixes_only_cover_actions_their_capability_may_block()
    {
        var actions = GatedActions();

        var overBlocked = ReadFixture().Mirrored
            .SelectMany(entry => actions
                .Where(a => (a.Prefix == entry.Prefix || a.Prefix.StartsWith(entry.Prefix + "/", StringComparison.Ordinal))
                    && a.Capability != entry.Capability
                    && !DependsOn(a.Capability, entry.Capability))
                .Select(a => $"{entry.Prefix} ({entry.Capability}) also covers {a.Prefix} ({a.Capability})"))
            .Distinct()
            .ToList();

        overBlocked.Should().BeEmpty(
            "a mirrored UI registry entry would block these actions in the browser while the server allows them");
    }
}
