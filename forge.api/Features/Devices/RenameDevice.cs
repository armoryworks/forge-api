using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Devices;

public record RenameDeviceCommand(int DeviceId, string Name) : IRequest;

public class RenameDeviceValidator : AbstractValidator<RenameDeviceCommand>
{
    public RenameDeviceValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class RenameDeviceHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext)
    : IRequestHandler<RenameDeviceCommand>
{
    public async Task Handle(RenameDeviceCommand request, CancellationToken cancellationToken)
    {
        var principal = httpContext.HttpContext!.User;
        var actorId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = principal.IsInRole("Admin");

        var device = await db.UserDevices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Device {request.DeviceId} not found");

        if (!isAdmin && device.UserId != actorId)
            throw new UnauthorizedAccessException("Not your device.");

        device.Name = request.Name;
        await db.SaveChangesAsync(cancellationToken);
    }
}
