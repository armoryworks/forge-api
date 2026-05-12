using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class NotificationRepository(AppDbContext db) : INotificationRepository
{
    public async Task<List<NotificationResponseModel>> GetByUserIdAsync(int userId, CancellationToken ct)
    {
        var notifications = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsDismissed)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

        var senderIds = notifications
            .Where(n => n.SenderId.HasValue)
            .Select(n => n.SenderId!.Value)
            .Distinct()
            .ToList();

        var senderLookup = new Dictionary<int, (string? Initials, string? AvatarColor)>();
        if (senderIds.Count > 0)
        {
            var senderData = await db.Users
                .Where(u => senderIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Initials, u.AvatarColor })
                .ToListAsync(ct);
            foreach (var s in senderData)
                senderLookup[s.Id] = (s.Initials, s.AvatarColor);
        }

        return notifications.Select(n =>
        {
            string? initials = null;
            string? color = null;
            if (n.SenderId.HasValue && senderLookup.TryGetValue(n.SenderId.Value, out var sender))
            {
                initials = sender.Initials;
                color = sender.AvatarColor;
            }

            return new NotificationResponseModel(
                n.Id, n.Type, n.Severity, n.Source, n.Title, n.Message,
                n.IsRead, n.IsPinned, n.IsDismissed,
                n.EntityType, n.EntityId,
                initials, color, n.CreatedAt);
        }).ToList();
    }

    public Task<Notification?> FindAsync(int id, CancellationToken ct)
        => db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task AddAsync(Notification notification, CancellationToken ct)
    {
        await db.Notifications.AddAsync(notification, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(int userId, CancellationToken ct)
    {
        await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    public async Task DismissAllAsync(int userId, CancellationToken ct)
    {
        await db.Notifications
            .Where(n => n.UserId == userId && !n.IsDismissed)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDismissed, true), ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);
}
