using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin.Telemetry;

/// <summary>
/// Every consent decision this install has recorded, newest first — acceptances and
/// declines alike, with who and from where. Rendered on the settings screen so the
/// record is visible to the customer who made it, not only to whoever queries the
/// database.
/// </summary>
public record GetTelemetryConsentHistoryQuery(int Take = 25)
    : IRequest<IReadOnlyList<TelemetryConsentRecordResponseModel>>;

public class GetTelemetryConsentHistoryHandler(AppDbContext db)
    : IRequestHandler<GetTelemetryConsentHistoryQuery, IReadOnlyList<TelemetryConsentRecordResponseModel>>
{
    public async Task<IReadOnlyList<TelemetryConsentRecordResponseModel>> Handle(
        GetTelemetryConsentHistoryQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 100);

        var rows = await db.AuditLogEntries.AsNoTracking()
            .Where(a => a.Action == TelemetrySettingKeys.ConsentAuditAction)
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new { a.CreatedAt, a.Details, a.IpAddress })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var (decision, version, by) = Parse(r.Details);
            return new TelemetryConsentRecordResponseModel(r.CreatedAt, decision, version, by, r.IpAddress);
        }).ToList();
    }

    // Details is JSON written by RecordTelemetryConsent. A row that predates a format
    // change still renders as "unknown" rather than throwing the whole history away.
    private static (string Decision, string Version, string? By) Parse(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return ("unknown", "unknown", null);

        try
        {
            using var doc = JsonDocument.Parse(details);
            var root = doc.RootElement;
            return (
                root.TryGetProperty("decision", out var d) ? d.GetString() ?? "unknown" : "unknown",
                root.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown",
                root.TryGetProperty("by", out var b) ? b.GetString() : null);
        }
        catch (JsonException)
        {
            return ("unknown", "unknown", null);
        }
    }
}
