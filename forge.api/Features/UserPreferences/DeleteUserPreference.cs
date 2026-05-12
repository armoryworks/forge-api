using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.UserPreferences;

public record DeleteUserPreferenceCommand(int UserId, string Key) : IRequest;

public class DeleteUserPreferenceHandler(IUserPreferenceRepository repo)
    : IRequestHandler<DeleteUserPreferenceCommand>
{
    public async Task Handle(DeleteUserPreferenceCommand request, CancellationToken cancellationToken)
    {
        var existing = await repo.FindByKeyAsync(request.UserId, request.Key, cancellationToken);

        if (existing is not null)
        {
            await repo.RemoveAsync(existing);
            await repo.SaveChangesAsync(cancellationToken);
        }
    }
}
