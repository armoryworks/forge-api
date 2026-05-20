using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record PairScanDeviceCommand(ScanDeviceRequestModel Data) : IRequest<ScanDeviceResponseModel>;

public class PairScanDeviceCommandValidator : AbstractValidator<PairScanDeviceCommand>
{
    public PairScanDeviceCommandValidator()
    {
        RuleFor(x => x.Data.DeviceId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.DeviceName).MaximumLength(200);
    }
}

public class PairScanDeviceHandler(
    IIdentityDbContext db,
    IClock clock,
    IHttpContextAccessor httpContext)
    : IRequestHandler<PairScanDeviceCommand, ScanDeviceResponseModel>
{
    public async Task<ScanDeviceResponseModel> Handle(
        PairScanDeviceCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;

        // Check if device is already paired
        var existing = await db.UserScanDevices
            .Where(d => d.DeviceId == request.Data.DeviceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
            throw new InvalidOperationException(
                $"Device '{request.Data.DeviceId}' is already paired to a user");

        var device = new UserScanDevice
        {
            UserId = userId,
            DeviceId = request.Data.DeviceId,
            DeviceName = request.Data.DeviceName,
            PairedAt = now,
            IsActive = true,
        };

        db.UserScanDevices.Add(device);
        await db.SaveChangesAsync(cancellationToken);

        return new ScanDeviceResponseModel(
            device.Id,
            device.DeviceId,
            device.DeviceName,
            device.PairedAt,
            device.IsActive);
    }
}
