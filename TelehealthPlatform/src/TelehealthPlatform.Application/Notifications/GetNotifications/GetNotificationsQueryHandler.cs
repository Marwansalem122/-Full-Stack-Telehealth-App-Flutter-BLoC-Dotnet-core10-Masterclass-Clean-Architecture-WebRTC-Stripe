using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Application.Notifications.GetNotifications;

public class GetNotificationsQueryHandler(INotificationRepository notificationRepository)
    : IRequestHandler<GetNotificationsQuery, GetNotificationsResult>
{
    public async Task<GetNotificationsResult> Handle(GetNotificationsQuery request, CancellationToken ct)
    {
        var (items, totalCount) = await notificationRepository.GetForUserAsync(
            request.UserId, request.UnreadOnly, request.Page, request.PageSize, ct);

        var dtos = items.Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.DataJson, n.IsRead, n.CreatedAtUtc)).ToList();

        return new GetNotificationsResult(dtos, request.Page, request.PageSize, totalCount);
    }
}