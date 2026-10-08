using System.Globalization;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Replenishment;

public record UpdateReplenishmentSettingsCommand(int? AssigneeUserId) : IRequest<ReplenishmentSettingsResponseModel>;

public class UpdateReplenishmentSettingsHandler(
    AppDbContext db,
    ISystemSettingRepository settings)
    : IRequestHandler<UpdateReplenishmentSettingsCommand, ReplenishmentSettingsResponseModel>
{
    public const string AssigneeRejectedMessage = "Pick an admin or manager; only they can approve suggestions.";

    public async Task<ReplenishmentSettingsResponseModel> Handle(
        UpdateReplenishmentSettingsCommand request, CancellationToken cancellationToken)
    {
        string description;
        if (request.AssigneeUserId is int userId)
        {
            var user = await db.Users
                .Where(u => u.Id == userId && u.IsActive)
                .Select(u => new { u.FirstName, u.LastName })
                .FirstOrDefaultAsync(cancellationToken);

            var isApprover = user is not null && await db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .AnyAsync(name => name == "Admin" || name == "Manager", cancellationToken);

            if (!isApprover)
                throw new ValidationException(
                    [new ValidationFailure(nameof(UpdateReplenishmentSettingsCommand.AssigneeUserId), AssigneeRejectedMessage)]);

            description = $"Replenishment assignee set to {user!.LastName}, {user.FirstName}";
        }
        else
        {
            description = "Replenishment assignee cleared";
        }

        var value = request.AssigneeUserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var setting = await settings.FindByKeyAsync(ReplenishmentAssignee.SettingKey, cancellationToken);
        if (setting is null)
        {
            setting = new SystemSetting
            {
                Key = ReplenishmentAssignee.SettingKey,
                Value = value,
                Description = "User who receives a to-do for each reorder suggestion",
            };
            await settings.AddAsync(setting, cancellationToken);
            await settings.SaveChangesAsync(cancellationToken);
        }
        else
        {
            setting.Value = value;
        }

        db.LogActivityAt("updated", description, ("SystemSetting", setting.Id));
        await settings.SaveChangesAsync(cancellationToken);

        return new ReplenishmentSettingsResponseModel(request.AssigneeUserId);
    }
}
