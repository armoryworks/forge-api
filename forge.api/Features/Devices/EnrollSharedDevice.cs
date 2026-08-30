using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Devices;

public record EnrollSharedDeviceCommand(
    string Token,
    string DeviceUuid,
    string DeviceName,
    string Platform,
    string? OsVersion,
    string? AppVersion) : IRequest<SharedDeviceEnrollResponseModel>;

public class EnrollSharedDeviceValidator : AbstractValidator<EnrollSharedDeviceCommand>
{
    public EnrollSharedDeviceValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceUuid).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeviceName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Platform).NotEmpty().Must(p => p is "ios" or "android");
    }
}

/// <summary>
/// Enrolls a device to the instance rather than to a person. The device
/// receives an opaque device credential (X-Device-Token) that proves it is
/// enrolled and un-revoked; every transaction on it still starts with a
/// badge scan or PIN that identifies the person for attribution.
/// </summary>
public class EnrollSharedDeviceHandler(
    AppDbContext db,
    IClock clock,
    IOptions<MobileOptions> options,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<EnrollSharedDeviceCommand, SharedDeviceEnrollResponseModel>
{
    public async Task<SharedDeviceEnrollResponseModel> Handle(
        EnrollSharedDeviceCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hash = OpaqueTokens.Sha256Hex(request.Token);

        var token = await db.DeviceEnrollmentTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null || token.ExpiresAt <= now || token.ConsumedAt is not null)
            throw new UnauthorizedAccessException("Enrollment code is invalid or expired.");
        if (!token.IsShared)
            throw new InvalidOperationException("This code enrolls a personal device.");

        var won = await db.DeviceEnrollmentTokens
            .Where(t => t.Id == token.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), cancellationToken);
        if (won == 0)
            throw new UnauthorizedAccessException("Enrollment code is invalid or expired.");

        var device = await db.UserDevices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.DeviceUuid == request.DeviceUuid, cancellationToken);
        if (device is null)
        {
            device = new UserDevice { DeviceUuid = request.DeviceUuid };
            db.UserDevices.Add(device);
        }

        var rawDeviceToken = OpaqueTokens.NewToken();
        device.UserId = null;
        device.Name = request.DeviceName;
        device.Platform = request.Platform;
        device.OsVersion = request.OsVersion;
        device.AppVersion = request.AppVersion;
        device.IsShared = true;
        device.EnrolledByUserId = token.IssuedByUserId;
        device.LastSeenAt = now;
        device.RevokedAt = null;
        device.RevokedByUserId = null;
        device.IsFlagged = false;
        device.DeletedAt = null;
        device.DeletedBy = null;
        device.DeviceTokenHash = OpaqueTokens.Sha256Hex(rawDeviceToken);
        await db.SaveChangesAsync(cancellationToken);

        await db.DeviceEnrollmentTokens
            .Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedByDeviceId, device.Id),
                cancellationToken);

        await auditWriter.WriteAsync(
            DeviceAuditEvents.Enrolled, token.IssuedByUserId,
            entityType: DeviceAuditEvents.EntityType, entityId: device.Id,
            details: JsonSerializer.Serialize(new { device.DeviceUuid, device.Platform, shared = true }),
            ct: cancellationToken);

        return new SharedDeviceEnrollResponseModel(
            device.Id, device.Name, rawDeviceToken, options.Value.InstanceName);
    }
}
