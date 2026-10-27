using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Scanner;

public record PairScanDeviceCommand(ScanDeviceRequestModel Data) : IRequest<ScanDeviceResponseModel>;

public class PairScanDeviceCommandValidator : AbstractValidator<PairScanDeviceCommand>
{
    public PairScanDeviceCommandValidator()
    {
        RuleFor(x => x.Data.DeviceId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.DeviceName).MaximumLength(200);
        RuleFor(x => x.Data.AssignedUserId).GreaterThan(0).When(x => x.Data.AssignedUserId.HasValue);
    }
}

public class PairScanDeviceHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContext)
    : IRequestHandler<PairScanDeviceCommand, ScanDeviceResponseModel>
{
    public async Task<ScanDeviceResponseModel> Handle(
        PairScanDeviceCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deviceId = request.Data.DeviceId.Trim();

        if (await db.UserScanDevices.AnyAsync(d => d.DeviceId == deviceId, cancellationToken))
            throw new InvalidOperationException($"Device '{deviceId}' is already paired");

        await ScanDeviceProjection.EnsureAssignableAsync(db, request.Data.AssignedUserId, cancellationToken);

        var device = new UserScanDevice
        {
            UserId = userId,
            DeviceId = deviceId,
            DeviceName = string.IsNullOrWhiteSpace(request.Data.DeviceName) ? null : request.Data.DeviceName.Trim(),
            AssignedUserId = request.Data.AssignedUserId,
            PairedAt = clock.UtcNow,
            IsActive = true,
        };

        db.UserScanDevices.Add(device);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("paired", $"Paired scanner {device.DeviceName ?? device.DeviceId}", ("UserScanDevice", device.Id));
        await db.SaveChangesAsync(cancellationToken);

        return (await ScanDeviceProjection.ToResponsesAsync(db, [device], cancellationToken))[0];
    }
}
