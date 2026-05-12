using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record SendComplianceReminderCommand(int UserId, int AdminId) : IRequest;

public class SendComplianceReminderHandler(AppDbContext db)
    : IRequestHandler<SendComplianceReminderCommand>
{
    public async Task Handle(SendComplianceReminderCommand request, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found.");

        var notification = new Notification
        {
            UserId = request.UserId,
            Type = "compliance_reminder",
            Severity = "warning",
            Source = "compliance",
            Title = "Compliance Forms Reminder",
            Message = "You have outstanding compliance forms that require your attention. Please review and complete them.",
            EntityType = "users",
            EntityId = request.UserId,
            SenderId = request.AdminId,
        };

        db.Set<Notification>().Add(notification);
        await db.SaveChangesAsync(ct);
    }
}
