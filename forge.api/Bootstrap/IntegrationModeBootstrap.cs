using Npgsql;

namespace Forge.Api.Bootstrap;

/// <summary>
/// Phase 1m.4 — startup helper that reads each integration's
/// <c>{provider}.mode</c> setting from <c>system_settings</c> directly
/// via ADO.NET, before the DI graph is built. Lets per-integration
/// Mock/Real/Disabled decisions drive the service registrations
/// without depending on the global <c>MockIntegrations</c> boolean.
///
/// Resolution order per integration:
///   1. If <c>system_settings</c> has a <c>{provider}.mode</c> row, use it.
///   2. Otherwise fall back to the global <c>MockIntegrations</c> flag
///      (true → Mock, false → Real).
///
/// Integration mode rows live in <c>system_settings</c> via
/// <c>SettingsService.SetAsync</c> (admin UI). The column is plain text
/// because it's not a secret, so we don't need <c>IDataProtector</c>
/// here.
///
/// Bootstrap is intentionally synchronous + plain ADO.NET so it can run
/// before DI/EF is wired. Failures (DB unreachable, table missing on a
/// fresh install before migrations land) are swallowed → falls back to
/// global flag.
/// </summary>
public sealed class IntegrationModeBootstrap
{
    public const string ModeMock = "Mock";
    public const string ModeReal = "Real";
    public const string ModeDisabled = "Disabled";

    private readonly Dictionary<string, string> _modes;
    private readonly bool _globalMocks;

    private IntegrationModeBootstrap(Dictionary<string, string> modes, bool globalMocks)
    {
        _modes = modes;
        _globalMocks = globalMocks;
    }

    /// <summary>True when the named integration should use the Mock impl.</summary>
    public bool IsMock(string provider)
        => Resolve(provider) == ModeMock;

    /// <summary>True when the named integration is fully disabled (caller
    /// should register no impl, or the no-op impl).</summary>
    public bool IsDisabled(string provider)
        => Resolve(provider) == ModeDisabled;

    /// <summary>True when the named integration should use the real impl.</summary>
    public bool IsReal(string provider)
        => Resolve(provider) == ModeReal;

    /// <summary>Effective mode for an integration. "Mock" / "Real" /
    /// "Disabled". Falls back to global flag when no row is set.</summary>
    public string Resolve(string provider)
    {
        if (_modes.TryGetValue($"{provider}.mode", out var stored)
            && !string.IsNullOrEmpty(stored))
        {
            return stored;
        }
        return _globalMocks ? ModeMock : ModeReal;
    }

    public static IntegrationModeBootstrap Load(IConfiguration configuration)
    {
        var globalMocks = configuration.GetValue<bool>("MockIntegrations");
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? configuration["ConnectionStrings:DefaultConnection"];

        var modes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(connectionString))
        {
            return new IntegrationModeBootstrap(modes, globalMocks);
        }

        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(
                "SELECT key, value FROM system_settings WHERE key LIKE '%.mode' AND deleted_at IS NULL",
                conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.GetString(1);
                modes[key] = value;
            }
        }
        catch
        {
            // First-boot scenario (table doesn't exist yet) or DB
            // unreachable — fall through to global flag.
        }

        return new IntegrationModeBootstrap(modes, globalMocks);
    }
}
