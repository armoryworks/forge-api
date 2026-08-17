using System.Reflection;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;

namespace Forge.Tests.Architecture;

/// <summary>
/// Promotes docs/coding-standards.md §0 to a failing test: every controller flows through the
/// capability gate. A controller is compliant when it carries <c>[RequiresCapability]</c> or
/// <c>[CapabilityBootstrap]</c> at class level, or when <b>every</b> HTTP action carries one of them.
/// A controller with neither is fail-open — its endpoints can never be turned off per install,
/// which silently breaks the preset/discovery model.
/// <para>
/// <see cref="LegacyUngated"/> is the debt register for controllers that predate enforcement. It
/// may only shrink: adding to it requires a CLAUDE.md conversation, and a controller that gains its
/// attribute must be removed from the list in the same commit (the second test enforces that).
/// </para>
/// </summary>
public sealed class ControllerCapabilityGateTests
{
    /// <summary>Controllers that predate §0 enforcement and still lack a capability attribute.
    /// Now empty — kept as the mechanism (a controller may only be added here with a CLAUDE.md
    /// conversation, and the second test evicts entries the moment they become gated).</summary>
    private static readonly HashSet<string> LegacyUngated = new(StringComparer.Ordinal)
    {
        // Emptied 2026-08-16: all 27 legacy controllers were assigned a capability or marked
        // bootstrap-exempt with a reason. Adding an entry here requires a CLAUDE.md conversation.
    };

    private static IEnumerable<Type> Controllers() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    private static bool IsGated(Type controller)
    {
        if (controller.GetCustomAttribute<RequiresCapabilityAttribute>(inherit: true) is not null ||
            controller.GetCustomAttribute<CapabilityBootstrapAttribute>(inherit: true) is not null)
            return true;

        var actions = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .ToList();

        return actions.Count > 0 && actions.All(m =>
            m.GetCustomAttribute<RequiresCapabilityAttribute>(inherit: true) is not null ||
            m.GetCustomAttribute<CapabilityBootstrapAttribute>(inherit: true) is not null);
    }

    [Fact]
    public void Every_controller_is_capability_gated_or_bootstrap_exempt()
    {
        var offenders = Controllers()
            .Where(c => !LegacyUngated.Contains(c.Name) && !IsGated(c))
            .Select(c => c.Name)
            .ToList();

        offenders.Should().BeEmpty(
            "docs/coding-standards.md §0: every controller must carry [RequiresCapability(\"CAP-…\")] " +
            "(reuse one from CapabilityCatalog.cs or register a new row) or [CapabilityBootstrap] " +
            "with a comment explaining why it must never be gated off. Offenders:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void Legacy_allowlist_only_shrinks()
    {
        var byName = Controllers().ToDictionary(c => c.Name, StringComparer.Ordinal);

        var stale = LegacyUngated
            .Where(name => !byName.TryGetValue(name, out var t) || IsGated(t))
            .ToList();

        stale.Should().BeEmpty(
            "these LegacyUngated entries are now gated (or gone) — remove them from the allowlist " +
            "so the debt register stays honest:\n  " + string.Join("\n  ", stale));
    }
}
