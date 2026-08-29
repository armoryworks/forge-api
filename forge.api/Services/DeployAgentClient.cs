using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// HTTP client for forge-agent. Registered only when Deploy:AgentUrl is configured; without it
/// <see cref="IsConfigured"/> is false and every call returns the unavailable shape so the admin
/// console can offer the terminal path instead of a button that cannot work.
/// </summary>
public partial class DeployAgentClient(HttpClient http, IConfiguration config, ILogger<DeployAgentClient> log)
    : IDeployAgentClient
{
    // A tier can be served by more than one container name. The UI upgrades by
    // blue/green cutover, which alternates between forge-ui and forge-ui-b — so
    // after every UI upgrade the live container is the *other* name. Matching
    // only the repo name made the whole tier vanish from the admin screen the
    // moment it was upgraded.
    private static readonly (string Repo, string Service, string[] Containers)[] Tiers =
    [
        ("forge-api", "api", ["forge-api"]),
        ("forge-ui", "ui", ["forge-ui", "forge-ui-b"]),
        ("forge-test", "test", ["forge-test"]),
        ("forge-demo", "demo", ["forge-demo"]),
    ];

    private static readonly DeployStateModel Unavailable = new(false, null, [], null);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Deploy:AgentUrl"]);

    public async Task<DeployStateModel> GetStateAsync(CancellationToken ct)
    {
        if (!IsConfigured) return Unavailable;
        var root = await GetJsonAsync("state", ct);
        if (root is null) return Unavailable;

        var state = root.Value.TryGetProperty("state", out var s) ? s : default;
        var images = RunningImages(root.Value);
        var tiers = Tiers
            .Select(t => (Tier: t, Running: t.Containers.Select(images.GetValueOrDefault).FirstOrDefault(v => v is not null)))
            .Where(x => x.Running is not null || HasRecord(state, x.Tier.Repo))
            .Select(x => new DeployTierModel(
                x.Tier.Service,
                x.Running,
                Str(state, x.Tier.Repo, "current"),
                Date(state, x.Tier.Repo, "deployedAt")))
            .ToList();

        var running = root.Value.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.Object
            ? await GetCurrentJobAsync(ct)
            : null;

        return new DeployStateModel(true, Str(root.Value, "agentVersion"), tiers, running);
    }

    public async Task<DeployAvailabilityModel> CheckAvailableAsync(CancellationToken ct)
    {
        if (!IsConfigured) return new DeployAvailabilityModel("unknown", null, "No upgrade agent on this box.");
        var root = await PostJsonAsync("actions", new { action = "check" }, ct);
        if (root is null) return new DeployAvailabilityModel("unknown", null, "The upgrade agent did not respond.");

        var output = Str(root.Value, "output") ?? string.Empty;
        var exit = root.Value.TryGetProperty("exitCode", out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetInt32()
            : -1;

        // Exit 10 is the CLI's "behind" signal. Anything else non-zero means the check itself
        // failed — an unreachable registry, most often. Reporting that as "current" would let an
        // install drift for years while the screen says it is fine, so it stays unknown.
        return exit switch
        {
            0 => new DeployAvailabilityModel("current", null, null),
            10 => new DeployAvailabilityModel("behind", NewestReleaseRe().Match(output) is { Success: true } m
                ? m.Groups[1].Value
                : null, null),
            _ => new DeployAvailabilityModel("unknown", null, output.Trim()),
        };
    }

    public async Task<DeployJobStartResultModel> StartJobAsync(
        string action, string? service, string? tag, string? confirm, CancellationToken ct)
    {
        if (!IsConfigured)
            return new DeployJobStartResultModel("unavailable", null, "No upgrade agent on this box.");

        using var response = await SendAsync(HttpMethod.Post, "jobs", new { action, svc = service, tag, confirm }, ct);
        if (response is null)
            return new DeployJobStartResultModel("unavailable", null, "The upgrade agent did not respond.");

        using var doc = await ReadAsync(response, ct);
        if (doc is null)
            return new DeployJobStartResultModel("unavailable", null, "The upgrade agent returned no body.");

        if (response.IsSuccessStatusCode)
            return new DeployJobStartResultModel("started", MapJob(doc.RootElement), null);

        var error = Str(doc.RootElement, "error");
        return (int)response.StatusCode == 409
            ? new DeployJobStartResultModel("busy", null, error)
            : new DeployJobStartResultModel("rejected", null, error);
    }

    public async Task<DeployJobModel?> GetJobAsync(string jobId, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var root = await GetJsonAsync($"jobs/{jobId}", ct);
        return root is null ? null : MapJob(root.Value);
    }

    public async Task<DeployJobModel?> GetCurrentJobAsync(CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var root = await GetJsonAsync("jobs/current", ct);
        return root is null || root.Value.ValueKind != JsonValueKind.Object ? null : MapJob(root.Value);
    }

    public async Task<DeployJobModel?> GetLastJobAsync(CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var root = await GetJsonAsync("jobs", ct);
        if (root is null || !root.Value.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
            return null;
        // The agent returns jobs newest-first.
        foreach (var j in jobs.EnumerateArray()) return MapJob(j);
        return null;
    }

    public async Task<string> GetJobLogAsync(string jobId, long offset, CancellationToken ct)
    {
        if (!IsConfigured) return string.Empty;
        using var response = await SendAsync(HttpMethod.Get, $"jobs/{jobId}/log?offset={offset}", null, ct);
        if (response is null || !response.IsSuccessStatusCode) return string.Empty;
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static bool HasRecord(JsonElement state, string repo) =>
        state.ValueKind == JsonValueKind.Object && state.TryGetProperty(repo, out _);

    private static Dictionary<string, string> RunningImages(JsonElement root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("containers", out var containers) || containers.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var c in containers.EnumerateArray())
        {
            var name = Str(c, "name");
            var image = Str(c, "image");
            if (name is null || image is null) continue;
            var colon = image.LastIndexOf(':');
            if (colon > 0) map[name] = image[(colon + 1)..];
        }
        return map;
    }

    private static DeployJobModel MapJob(JsonElement j) => new(
        Str(j, "id") ?? string.Empty,
        Str(j, "action") ?? string.Empty,
        Str(j, "svc"),
        Str(j, "tag"),
        Str(j, "state") ?? "unknown",
        j.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : null,
        Date(j, "startedAt") ?? default,
        Date(j, "endedAt"),
        MapApproval(j),
        Str(j, "reason"),
        j.TryGetProperty("logSize", out var ls) && ls.ValueKind == JsonValueKind.Number ? ls.GetInt64() : 0);

    private static DeployApprovalModel? MapApproval(JsonElement j)
    {
        if (!j.TryGetProperty("needsApproval", out var a) || a.ValueKind != JsonValueKind.Object) return null;

        var statements = a.TryGetProperty("statements", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
                .Select(x => new DeployDestructiveStatementModel(
                    x.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0,
                    Str(x, "statement") ?? string.Empty))
                .ToList()
            : [];

        var dispositions = a.TryGetProperty("dispositions", out var d) && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : [];

        return new DeployApprovalModel(
            statements,
            a.TryGetProperty("preMigrateCommitted", out var p) && p.ValueKind == JsonValueKind.True,
            dispositions);
    }

    private static string? Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? Str(JsonElement e, string obj, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(obj, out var o) ? Str(o, prop) : null;

    private static DateTimeOffset? Date(JsonElement e, string prop) =>
        DateTimeOffset.TryParse(Str(e, prop), out var d) ? d : null;

    private static DateTimeOffset? Date(JsonElement e, string obj, string prop) =>
        DateTimeOffset.TryParse(Str(e, obj, prop), out var d) ? d : null;

    private async Task<JsonElement?> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        using var doc = await ReadAsync(response, ct);
        return doc?.RootElement.Clone();
    }

    private async Task<JsonElement?> PostJsonAsync(string path, object body, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        using var doc = await ReadAsync(response, ct);
        return doc?.RootElement.Clone();
    }

    private static async Task<JsonDocument?> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "null") return null;
        return JsonDocument.Parse(text);
    }

    // The agent going away mid-upgrade is the expected case, not an exception worth propagating:
    // it is on the same box being redeployed. Callers get the unavailable shape and the console
    // holds its lock until the marker file or the reconnect broadcast resolves it.
    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "forge-agent unreachable at {Path}", path);
            return null;
        }
    }

    [GeneratedRegex(@"newest release is (\S+)")]
    private static partial Regex NewestReleaseRe();
}
