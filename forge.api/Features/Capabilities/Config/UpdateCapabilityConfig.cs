using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Capabilities.Descriptor;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Capabilities.Config;

/// <summary>
/// Phase 4 Phase-C — Updates the opaque <see cref="CapabilityConfig.ConfigJson"/>
/// payload for a single capability. Optimistic concurrency via If-Match
/// against the CapabilityConfig row's <see cref="CapabilityConfig.Version"/>
/// (independent of the parent Capability's Version). Emits an audit row
/// with eventType <c>CapabilityConfigChanged</c> capturing before/after.
///
/// Phase C scope: opaque string (no per-capability schema validation). Phase
/// E/F may add validators when capabilities start using config.
/// </summary>
public record UpdateCapabilityConfigCommand(
    string Code,
    string ConfigJson,
    string? IfMatch = null,
    string? Reason = null) : IRequest<CapabilityDescriptorEntry>;

public class UpdateCapabilityConfigValidator : AbstractValidator<UpdateCapabilityConfigCommand>
{
    public UpdateCapabilityConfigValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^CAP-[A-Z0-9-]+$");

        RuleFor(x => x.ConfigJson)
            .NotNull()
            .Must(BeWellFormedJson).WithMessage("configJson must be valid JSON.")
            .MaximumLength(64 * 1024); // 64 KiB upper bound — config payloads are small.

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => x.Reason is not null);
    }

    private static bool BeWellFormedJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public class UpdateCapabilityConfigHandler(
    AppDbContext db,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<UpdateCapabilityConfigCommand, CapabilityDescriptorEntry>
{
    public async Task<CapabilityDescriptorEntry> Handle(
        UpdateCapabilityConfigCommand request,
        CancellationToken cancellationToken)
    {
        var capability = await db.Capabilities
            .Include(c => c.Configs)
            .FirstOrDefaultAsync(c => c.Code == request.Code, cancellationToken)
            ?? throw new KeyNotFoundException($"Capability '{request.Code}' not found.");

        var config = capability.Configs.FirstOrDefault();
        var beforeJson = config?.ConfigJson ?? "{}";

        // If-Match check (Phase C — separate ETag space from Capability.Version
        // because configs and toggles are independent edit surfaces).
        if (!string.IsNullOrWhiteSpace(request.IfMatch) && config is not null)
        {
            var trimmed = request.IfMatch.Trim();
            if (trimmed.StartsWith("W/", StringComparison.Ordinal)) trimmed = trimmed[2..];
            trimmed = trimmed.Trim('"').Trim();

            if (!uint.TryParse(trimmed, out var providedVersion) || providedVersion != config.Version)
            {
                throw new CapabilityMutationException(
                    StatusCodes.Status412PreconditionFailed,
                    "version-mismatch",
                    $"Stale ETag for capability '{request.Code}' config — refresh and try again.",
                    new Dictionary<string, object?>
                    {
                        ["capability"] = request.Code,
                        ["expected"] = config.Version,
                        ["received"] = request.IfMatch,
                    });
            }
        }

        if (config is null)
        {
            config = new CapabilityConfig
            {
                CapabilityId = capability.Id,
                ConfigJson = request.ConfigJson,
                SchemaVersion = 1,
            };
            db.CapabilityConfigs.Add(config);
        }
        else
        {
            config.ConfigJson = request.ConfigJson;
        }

        await db.SaveChangesAsync(cancellationToken);

        var actorId = db.CurrentUserId ?? 0;
        var details = JsonSerializer.Serialize(new
        {
            code = capability.Code,
            before = new { configJson = beforeJson },
            after = new { configJson = request.ConfigJson },
            reason = request.Reason,
            actorUserId = actorId,
        });

        await auditWriter.WriteAsync(
            action: CapabilityAuditEvents.ConfigChanged,
            userId: actorId,
            entityType: CapabilityAuditEvents.EntityType,
            entityId: capability.Id,
            details: details,
            ct: cancellationToken);

        return new CapabilityDescriptorEntry(
            Id: capability.Code,
            Code: capability.Code,
            Area: capability.Area,
            Name: capability.Name,
            Description: capability.Description,
            Enabled: capability.Enabled,
            IsDefaultOn: capability.IsDefaultOn,
            RequiresRoles: capability.RequiresRoles,
            Version: capability.Version,
            ETag: $"W/\"{capability.Version}\"",
            ConfigVersion: config.Version,
            ConfigETag: $"W/\"{config.Version}\"",
            ConfigId: config.Id,
            Dependencies: Array.Empty<string>(),
            Mutexes: Array.Empty<string>());
    }
}
