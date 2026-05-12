using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface INotificationRepository
{
    Task<List<NotificationResponseModel>> GetByUserIdAsync(int userId, CancellationToken ct);
    Task<Notification?> FindAsync(int id, CancellationToken ct);
    Task AddAsync(Notification notification, CancellationToken ct);
    Task MarkAllReadAsync(int userId, CancellationToken ct);
    Task DismissAllAsync(int userId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
