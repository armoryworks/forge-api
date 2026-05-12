using System.Security.Claims;

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Auth;

public record GetMfaStatusQuery(ClaimsPrincipal User) : IRequest<MfaStatusResponseModel>;

public class GetMfaStatusHandler(IMfaService mfaService) : IRequestHandler<GetMfaStatusQuery, MfaStatusResponseModel>
{
    public async Task<MfaStatusResponseModel> Handle(GetMfaStatusQuery request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(request.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException());

        return await mfaService.GetMfaStatusAsync(userId, cancellationToken);
    }
}
