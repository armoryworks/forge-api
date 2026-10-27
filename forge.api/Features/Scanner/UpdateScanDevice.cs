using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Scanner;

public record UpdateScanDeviceCommand(int Id, UpdateScanDeviceRequestModel Data) : IRequest<ScanDeviceResponseModel>;

public class UpdateScanDeviceCommandValidator : AbstractValidator<UpdateScanDeviceCommand>
{
    public UpdateScanDeviceCommandValidator()
    {
        RuleFor(x => x.Data.DeviceName).MaximumLength(200);
        RuleFor(x => x.Data.AssignedUserId).GreaterThan(0).When(x => x.Data.AssignedUserId.HasValue);
    }
}

public class UpdateScanDeviceHandler(AppDbContext db)
    : IRequestHandler<UpdateScanDeviceCommand, ScanDeviceResponseModel>
{
    public async Task<ScanDeviceResponseModel> Handle(UpdateScanDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = await db.UserScanDevices
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Device {request.Id} not found");

        if (request.Data.AssignedUserId != device.AssignedUserId)
            await ScanDeviceProjection.EnsureAssignableAsync(db, request.Data.AssignedUserId, cancellationToken);

        var name = string.IsNullOrWhiteSpace(request.Data.DeviceName) ? null : request.Data.DeviceName.Trim();
        var changed = new List<string>();
        if (name != device.DeviceName) changed.Add("name");
        if (request.Data.IsActive != device.IsActive) changed.Add("active");
        var reassigned = request.Data.AssignedUserId != device.AssignedUserId;
        if (reassigned) changed.Add("assigned user");

        device.DeviceName = name;
        device.IsActive = request.Data.IsActive;
        device.AssignedUserId = request.Data.AssignedUserId;

        if (ActivityChangeSummary.Updated(changed) is string summary)
        {
            db.LogActivityAt(
                reassigned ? "assigned" : "updated",
                $"{summary} on scanner {device.DeviceName ?? device.DeviceId}",
                ("UserScanDevice", device.Id));
        }

        await db.SaveChangesAsync(cancellationToken);

        return (await ScanDeviceProjection.ToResponsesAsync(db, [device], cancellationToken))[0];
    }
}
