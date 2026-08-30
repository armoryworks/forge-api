using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Reports this install's health to Armory Works, but only ever with permission.
///
/// The consent gate is the first thing <see cref="RunAsync"/> does and the only place
/// it is decided. Everything downstream — enrolling, polling, heartbeating — assumes
/// permission was already established, so there is one line to read to know whether
/// an install can talk, rather than a condition repeated at three call sites where one
/// of them will eventually be wrong.
///
/// Failures are swallowed deliberately and recorded as <c>telemetry.last_error</c>.
/// A customer's ERP must never degrade because a vendor's monitoring endpoint is
/// unreachable; monitoring is a convenience for AWT, and it is not allowed to become
/// a dependency for the shop floor.
/// </summary>
public sealed class TelemetryReporter(
    AppDbContext db,
    HttpClient http,
    HealthCheckService healthChecks,
    IOptions<TelemetryOptions> options,
    IClock clock,
    ILogger<TelemetryReporter> logger) : ITelemetryReporter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task RunAsync(CancellationToken ct = default)
    {
        var endpoint = options.Value.Endpoint?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint)) return;

        var settings = await LoadAsync(ct);

        // The gate. No accepted agreement, nothing leaves — not a heartbeat, not an
        // enrollment, not the install's existence.
        if (!string.Equals(settings.GetValueOrDefault(TelemetrySettingKeys.Enabled), "true", StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.Equals(settings.GetValueOrDefault(TelemetrySettingKeys.ConsentDecision), "accepted", StringComparison.OrdinalIgnoreCase))
            return;
        // A consent recorded against superseded terms is not consent to the current
        // ones. Stop until the operator has seen what changed and agreed again.
        if (settings.GetValueOrDefault(TelemetrySettingKeys.ConsentVersion) != TelemetryAgreement.Version)
            return;

        try
        {
            var token = settings.GetValueOrDefault(TelemetrySettingKeys.Token);
            if (!string.IsNullOrWhiteSpace(token))
            {
                await SendHeartbeatAsync(endpoint, token!, ct);
            }
            else if (!string.IsNullOrWhiteSpace(settings.GetValueOrDefault(TelemetrySettingKeys.PendingToken)))
            {
                await CollectDecisionAsync(endpoint, settings, ct);
            }
            else
            {
                await EnrollAsync(endpoint, settings, ct);
            }

            await SetAsync(TelemetrySettingKeys.LastError, "", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Recorded, surfaced on the settings screen, and otherwise ignored.
            logger.LogWarning(ex, "Telemetry cycle failed");
            await SetAsync(TelemetrySettingKeys.LastError, Truncate(ex.Message, 400), ct);
        }
    }

    private async Task EnrollAsync(string endpoint, Dictionary<string, string> settings, CancellationToken ct)
    {
        var installId = settings.GetValueOrDefault(TelemetrySettingKeys.InstallId);
        if (string.IsNullOrWhiteSpace(installId))
        {
            installId = Guid.NewGuid().ToString("N");
            await SetAsync(TelemetrySettingKeys.InstallId, installId, ct);
        }

        var body = new
        {
            installId,
            baseUrl = string.IsNullOrWhiteSpace(options.Value.PublicUrl) ? null : options.Value.PublicUrl.TrimEnd('/'),
            product = "forge",
            companyName = settings.GetValueOrDefault("company.name"),
            contactEmail = settings.GetValueOrDefault(TelemetrySettingKeys.ConsentBy),
            version = AppVersion,
            consentVersion = settings.GetValueOrDefault(TelemetrySettingKeys.ConsentVersion),
            consentAcceptedAt = settings.GetValueOrDefault(TelemetrySettingKeys.ConsentAt),
            consentAcceptedBy = settings.GetValueOrDefault(TelemetrySettingKeys.ConsentBy),
        };

        using var response = await http.PostAsJsonAsync($"{endpoint}/api/public/telemetry/enroll", body, Json, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(Json, ct);
        if (!string.IsNullOrWhiteSpace(result?.PendingToken))
            await SetAsync(TelemetrySettingKeys.PendingToken, result!.PendingToken!, ct);

        await SetAsync(TelemetrySettingKeys.EnrollmentStatus, result?.Status ?? "Pending", ct);
    }

    private async Task CollectDecisionAsync(string endpoint, Dictionary<string, string> settings, CancellationToken ct)
    {
        var installId = settings.GetValueOrDefault(TelemetrySettingKeys.InstallId);
        var pending = settings.GetValueOrDefault(TelemetrySettingKeys.PendingToken);
        if (string.IsNullOrWhiteSpace(installId) || string.IsNullOrWhiteSpace(pending)) return;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{endpoint}/api/public/telemetry/enroll/{Uri.EscapeDataString(installId!)}");
        request.Headers.Authorization = new("Bearer", pending);

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The far side has forgotten this enrollment (or it was rejected and the
            // token revoked). Start over rather than polling a dead id forever.
            await SetAsync(TelemetrySettingKeys.PendingToken, "", ct);
            await SetAsync(TelemetrySettingKeys.EnrollmentStatus, "NotEnrolled", ct);
            return;
        }
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EnrollmentStatusResponse>(Json, ct);
        await SetAsync(TelemetrySettingKeys.EnrollmentStatus, result?.Status ?? "Pending", ct);

        if (!string.IsNullOrWhiteSpace(result?.TelemetryToken))
        {
            await SetAsync(TelemetrySettingKeys.Token, result!.TelemetryToken!, ct);
            // Burned on the far side too — keeping it would only be a stale secret.
            await SetAsync(TelemetrySettingKeys.PendingToken, "", ct);
        }
    }

    private async Task SendHeartbeatAsync(string endpoint, string token, CancellationToken ct)
    {
        var report = await healthChecks.CheckHealthAsync(ct);

        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description })
                .ToList(),
            version = AppVersion,
            uptimeSeconds = (clock.UtcNow - ProcessStart).TotalSeconds,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/public/telemetry/heartbeat")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Authorization = new("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Revoked at the far end — monitoring was paused or the enrollment undone.
            // Drop the token and let the next cycle re-enroll; the operator's consent
            // is unchanged, so this doesn't need them.
            await SetAsync(TelemetrySettingKeys.Token, "", ct);
            await SetAsync(TelemetrySettingKeys.EnrollmentStatus, "NotEnrolled", ct);
            return;
        }
        response.EnsureSuccessStatusCode();

        await SetAsync(TelemetrySettingKeys.LastHeartbeatAt, clock.UtcNow.ToString("O"), ct);
        await SetAsync(TelemetrySettingKeys.EnrollmentStatus, "Accepted", ct);
    }

    private static readonly DateTimeOffset ProcessStart =
        new(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);

    private static string AppVersion =>
        Environment.GetEnvironmentVariable("APP_VERSION") is { Length: > 0 } v ? v : "dev";

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct) =>
        await db.SystemSettings
            .Where(s => s.Key.StartsWith("telemetry.") || s.Key == "company.name")
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    private async Task SetAsync(string key, string value, CancellationToken ct)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null) db.SystemSettings.Add(new Core.Entities.SystemSetting { Key = key, Value = value });
        else setting.Value = value;
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record EnrollmentResponse(string InstallId, string Status, string? PendingToken);

    private sealed record EnrollmentStatusResponse(string Status, string? TelemetryToken, int? HeartbeatIntervalSeconds);
}
