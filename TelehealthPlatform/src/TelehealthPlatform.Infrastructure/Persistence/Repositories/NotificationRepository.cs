using Microsoft.EntityFrameworkCore;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Repositories;

public class NotificationRepository(TelehealthDbContext db) : INotificationRepository
{
    public async Task AddAsync(Notification notification, CancellationToken ct) => await db.Notifications.AddAsync(notification, ct);

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<(List<Notification> Items, int TotalCount)> GetForUserAsync(
        Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}