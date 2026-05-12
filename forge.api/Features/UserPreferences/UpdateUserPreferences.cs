using System.Text.Json;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.UserPreferences;

public record UpdateUserPreferencesCommand(
    int UserId,
    Dictionary<string, object?> Preferences) : IRequest<List<UserPreferenceResponseModel>>;

public class UpdateUserPreferencesHandler(IUserPreferenceRepository repo)
    : IRequestHandler<UpdateUserPreferencesCommand, List<UserPreferenceResponseModel>>
{
    public async Task<List<UserPreferenceResponseModel>> Handle(
        UpdateUserPreferencesCommand request, CancellationToken cancellationToken)
    {
        foreach (var (key, value) in request.Preferences)
        {
            var existing = await repo.FindByKeyAsync(request.UserId, key, cancellationToken);

            if (existing is not null)
            {
                existing.ValueJson = JsonSerializer.Serialize(value);
            }
            else
            {
                await repo.AddAsync(new UserPreference
                {
                    UserId = request.UserId,
                    Key = key,
                    ValueJson = JsonSerializer.Serialize(value),
                }, cancellationToken);
            }
        }

        await repo.SaveChangesAsync(cancellationToken);
        return await repo.GetByUserIdAsync(request.UserId, cancellationToken);
    }
}
