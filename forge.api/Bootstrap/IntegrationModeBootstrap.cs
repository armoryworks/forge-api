using Npgsql;

using Forge.Core.Settings;

namespace Forge.Api.Bootstrap;

/// <summary>
/// Phase 1m.4 / Phase 3 — startup helper that reads each integration's
/// <c>{provider}.mode</c> setting from <c>system_settings</c> directly via ADO.NET,
/// before the DI graph is built, so per-integration Mock/Real/Auto/Disabled decisions
/// can drive the service registrations without depending on the global
/// <c>MockIntegrations</c> boolean alone.
///
/// Resolution (see <see cref="ResolveMode"/>):
/// <list type="bullet">
///   <item>An explicit <c>Mock</c> / <c>Real</c> / <c>Disabled</c> mode always wins.</item>
///   <item><c>Auto</c> (or no mode row) resolves by whether the integration is configured —
///   its primary-credential setting (the descriptor's <c>IsConfiguredCheckKey</c>) has a
///   value. Configured → Real, unconfigured → Mock.</item>
///   <item>The global <c>MockIntegrations</c> flag is the <em>posture</em>: in a mock posture
///   an integration stays Mock unless explicitly set to Real (dev safety — configuring a
///   credential never silently starts hitting a live service in dev). In a real posture
///   (production) an integration adopts its real impl once configured and otherwise falls
///   back to Mock so calls don't crash — the readiness report surfaces the gap.</item>
/// </list>
///
/// Bootstrap is intentionally synchronous + plain ADO.NET so it can run before DI/EF is
/// wired. Failures (DB unreachable, table missing on a fresh install) are swallowed → falls
/// back to the global flag.
/// </summary>
public sealed class IntegrationModeBootstrap
{
    public const string ModeMock = "Mock";
    public const string ModeReal = "Real";
    public const string ModeAuto = "Auto";
    public const string ModeDisabled = "Disabled";

    private readonly Dictionary<string, string> _modes;
    private readonly Dictionary<string, string> _configuredCheckKeys; // provider → primary-credential key
    private readonly HashSet<string> _configuredValues;               // check-keys that carry a value
    private readonly bool _globalMocks;

    private IntegrationModeBootstrap(
        Dictionary<string, string> modes,
        Dictionary<string, string> configuredCheckKeys,
        HashSet<string> configuredValues,
        bool globalMocks)
    {
        _modes = modes;
        _configuredCheckKeys = configuredCheckKeys;
        _configuredValues = configuredValues;
        _globalMocks = globalMocks;
    }

    /// <summary>True when the named integration should use the Mock impl.</summary>
    public bool IsMock(string provider) => Resolve(provider) == ModeMock;

    /// <summary>True when the named integration is fully disabled (caller should register
    /// no impl, or the no-op impl).</summary>
    public bool IsDisabled(string provider) => Resolve(provider) == ModeDisabled;

    /// <summary>True when the named integration should use the real impl.</summary>
    public bool IsReal(string provider) => Resolve(provider) == ModeReal;

    /// <summary>Effective mode for an integration: "Mock" / "Real" / "Disabled".</summary>
    public string Resolve(string provider)
        => ResolveMode(_modes.GetValueOrDefault($"{provider}.mode"), IsConfigured(provider), _globalMocks);

    /// <summary>Whether the integration has its primary credential set. Null when the
    /// provider has no descriptor / no pinned check key (configured-state unknown).</summary>
    private bool? IsConfigured(string provider)
    {
        if (!_configuredCheckKeys.TryGetValue(provider, out var checkKey) || string.IsNullOrEmpty(checkKey))
            return null;
        return _configuredValues.Contains(checkKey);
    }

    /// <summary>Pure resolution logic — no I/O, unit-testable. Never returns "Auto":
    /// Auto is collapsed to Mock/Real here so callers only ever see a concrete impl choice.</summary>
    public static string ResolveMode(string? explicitMode, bool? configured, bool globalMocks)
    {
        if (string.Equals(explicitMode, ModeDisabled, StringComparison.OrdinalIgnoreCase)) return ModeDisabled;
        if (string.Equals(explicitMode, ModeMock, StringComparison.OrdinalIgnoreCase)) return ModeMock;
        if (string.Equals(explicitMode, ModeReal, StringComparison.OrdinalIgnoreCase)) return ModeReal;

        var isAuto = string.Equals(explicitMode, ModeAuto, StringComparison.OrdinalIgnoreCase);
        if (isAuto)
        {
            // Explicit opt-in to configured-based resolution, independent of posture.
            return configured switch
            {
                true => ModeReal,
                false => ModeMock,
                null => globalMocks ? ModeMock : ModeReal,
            };
        }

        // No explicit mode → the global flag is the posture.
        if (globalMocks) return ModeMock;                 // dev/mock: mock unless explicitly Real
        return configured == false ? ModeMock : ModeReal; // prod: real when configured (or unknown), else mock
    }

    public static IntegrationModeBootstrap Load(IConfiguration configuration)
    {
        var globalMocks = configuration.GetValue<bool>("MockIntegrations");
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? configuration["ConnectionStrings:DefaultConnection"];

        // provider → primary-credential key, from the descriptor catalog.
        var checkKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in IntegrationDescriptorCatalog.All)
            if (!string.IsNullOrEmpty(d.IsConfiguredCheckKey))
                checkKeys[d.Provider] = d.IsConfiguredCheckKey!;

        var modes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var configuredValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(connectionString))
            return new IntegrationModeBootstrap(modes, checkKeys, configuredValues, globalMocks);

        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            // Pull the mode rows and the configured-check rows in one pass. Check-key values
            // may be sealed secrets — we only test presence, never unseal, so a sealed
            // envelope (a non-empty string) reads as "configured", which is correct.
            using var cmd = new NpgsqlCommand(
                "SELECT key, value FROM system_settings " +
                "WHERE (key LIKE '%.mode' OR key = ANY(@keys)) AND deleted_at IS NULL",
                conn);
            cmd.Parameters.AddWithValue("keys", checkKeys.Values.Distinct().ToArray());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (key.EndsWith(".mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(value)) modes[key] = value!;
                }
                else if (!string.IsNullOrWhiteSpace(value))
                {
                    configuredValues.Add(key);
                }
            }
        }
        catch
        {
            // First-boot (table absent) or DB unreachable — fall through to global flag.
        }

        return new IntegrationModeBootstrap(modes, checkKeys, configuredValues, globalMocks);
    }
}
