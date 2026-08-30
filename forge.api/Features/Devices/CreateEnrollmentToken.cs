using System.Security.Claims;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Devices;

public record CreateEnrollmentTokenCommand(int TargetUserId, bool IsShared)
    : IRequest<EnrollmentTokenResponseModel>;

public class CreateEnrollmentTokenValidator : AbstractValidator<CreateEnrollmentTokenCommand>
{
    public CreateEnrollmentTokenValidator()
    {
        RuleFor(x => x.TargetUserId).GreaterThan(0).When(x => !x.IsShared);
    }
}

public class CreateEnrollmentTokenHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IClock clock,
    IOptions<MobileOptions> options,
    IHttpContextAccessor httpContext,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<CreateEnrollmentTokenCommand, EnrollmentTokenResponseModel>
{
    public async Task<EnrollmentTokenResponseModel> Handle(
        CreateEnrollmentTokenCommand request, CancellationToken cancellationToken)
    {
        var adminId = int.Parse(
            httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!request.IsShared)
        {
            var target = await userManager.FindByIdAsync(request.TargetUserId.ToString())
                ?? throw new KeyNotFoundException($"User {request.TargetUserId} not found");
            if (!target.IsActive)
                throw new InvalidOperationException("Cannot enroll a device for an inactive user.");
        }

        var now = clock.UtcNow;
        var raw = OpaqueTokens.NewToken();
        var expiresAt = now.AddMinutes(options.Value.EnrollmentTokenLifetimeMinutes);

        db.DeviceEnrollmentTokens.Add(new DeviceEnrollmentToken
        {
            TokenHash = OpaqueTokens.Sha256Hex(raw),
            TargetUserId = request.IsShared ? null : request.TargetUserId,
            IsShared = request.IsShared,
            IssuedByUserId = adminId,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            DeviceAuditEvents.EnrollmentTokenIssued, adminId,
            entityType: DeviceAuditEvents.EntityType,
            details: JsonSerializer.Serialize(new { targetUserId = request.TargetUserId, shared = request.IsShared }),
            ct: cancellationToken);

        return new EnrollmentTokenResponseModel(
            raw, expiresAt, options.Value.InstanceName, options.Value.CertSha256, request.IsShared);
    }
}
