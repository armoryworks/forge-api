using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Admin;

public record GetMfaPolicyQuery : IRequest<MfaPolicyResponseModel>;

public class GetMfaPolicyHandler(AppDbContext db) : IRequestHandler<GetMfaPolicyQuery, MfaPolicyResponseModel>
{
    public async Task<MfaPolicyResponseModel> Handle(GetMfaPolicyQuery request, CancellationToken cancellationToken)
        => new()
        {
            RequiredRoles = await MfaRolePolicy.GetEffectiveRequiredRolesAsync(db, cancellationToken),
            EnrollmentGraceDays = await MfaRolePolicy.GetGraceDaysAsync(db, cancellationToken),
        };
}
