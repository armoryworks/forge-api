using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin.Telemetry;

/// <summary>
/// Records the operator's answer to the monitoring agreement — either answer.
///
/// A decline is written to the audit log exactly as an acceptance is. That is the
/// point of keeping both: "we asked, and on this date they said no" is a fact worth
/// being able to produce, and storing only the yeses would make the record a sales
/// artefact rather than a consent record.
///
/// The version is stamped from what the server currently ships rather than taken from
/// the client, so a stale browser tab can't record agreement to superseded terms.
/// Declining also revokes any credentials this install holds: consent withdrawn means
/// it stops reporting immediately, not at the end of some cycle.
/// </summary>
public record RecordTelemetryConsentCommand(bool Accepted, string? AcceptedByEmail, string? IpAddress, string? UserAgent)
    : IRequest<TelemetryStatusResponseModel>;

public class RecordTelemetryConsentValidator : AbstractValidator<RecordTelemetryConsentCommand>
{
    public RecordTelemetryConsentValidator()
    {
        RuleFor(x => x.AcceptedByEmail).MaximumLength(256)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.AcceptedByEmail));
    }
}

public class RecordTelemetryConsentHandler(AppDbContext db, IMediator mediator, IClock clock)
    : IRequestHandler<RecordTelemetryConsentCommand, TelemetryStatusResponseModel>
{
    public async Task<TelemetryStatusResponseModel> Handle(RecordTelemetryConsentCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var decision = request.Accepted ? "accepted" : "declined";

        await SetAsync(TelemetrySettingKeys.Enabled, request.Accepted ? "true" : "false", ct);
        await SetAsync(TelemetrySettingKeys.ConsentDecision, decision, ct);
        // Stamped server-side: a stale tab must not be able to consent to old terms.
        await SetAsync(TelemetrySettingKeys.ConsentVersion, TelemetryAgreement.Version, ct);
        await SetAsync(TelemetrySettingKeys.ConsentAt, now.ToString("O"), ct);
        await SetAsync(TelemetrySettingKeys.ConsentBy, request.AcceptedByEmail ?? "", ct);

        if (!request.Accepted)
        {
            // Withdrawn consent takes effect now, not eventually. Without the token
            // the reporter cannot send, whatever else is stale in settings.
            await SetAsync(TelemetrySettingKeys.Token, "", ct);
            await SetAsync(TelemetrySettingKeys.PendingToken, "", ct);
            await SetAsync(TelemetrySettingKeys.EnrollmentStatus, "NotEnrolled", ct);
            await SetAsync(TelemetrySettingKeys.LastError, "", ct);
        }

        db.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = db.CurrentUserId ?? 0,
            Action = TelemetrySettingKeys.ConsentAuditAction,
            EntityType = "TelemetryConsent",
            CreatedAt = now,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            Details = JsonSerializer.Serialize(new
            {
                decision,
                version = TelemetryAgreement.Version,
                by = request.AcceptedByEmail,
            }),
        });
        await db.SaveChangesAsync(ct);

        return await mediator.Send(new GetTelemetryStatusQuery(), ct);
    }

    private async Task SetAsync(string key, string value, CancellationToken ct)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = "Remote health monitoring" });
        else
            setting.Value = value;
    }
}
