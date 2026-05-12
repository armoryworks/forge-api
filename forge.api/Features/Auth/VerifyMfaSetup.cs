using System.Security.Claims;

using FluentValidation;
using MediatR;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Auth;

public record VerifyMfaSetupCommand(ClaimsPrincipal User, int DeviceId, string Code) : IRequest<bool>;

public class VerifyMfaSetupValidator : AbstractValidator<VerifyMfaSetupCommand>
{
    public VerifyMfaSetupValidator()
    {
        RuleFor(x => x.DeviceId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches(@"^\d{6}$");
    }
}

public class VerifyMfaSetupHandler(
    IMfaService mfaService,
    ISystemAuditWriter auditWriter) : IRequestHandler<VerifyMfaSetupCommand, bool>
{
    public async Task<bool> Handle(VerifyMfaSetupCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(request.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException());

        var result = await mfaService.VerifyTotpSetupAsync(userId, request.DeviceId, request.Code, cancellationToken);

        if (result)
        {
            await auditWriter.WriteAsync("MfaEnabled", userId,
                entityType: "ApplicationUser",
                entityId: userId,
                details: System.Text.Json.JsonSerializer.Serialize(new { deviceId = request.DeviceId }),
                ct: cancellationToken);
        }

        return result;
    }
}
