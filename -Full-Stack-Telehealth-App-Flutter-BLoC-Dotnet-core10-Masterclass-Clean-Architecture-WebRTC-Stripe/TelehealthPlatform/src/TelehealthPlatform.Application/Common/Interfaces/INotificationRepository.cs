using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Common.Interfaces;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct);
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<(List<Notification> Items, int TotalCount)> GetForUserAsync(
        Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct);
}