using MediatR;

namespace TelehealthPlatform.Application.Notifications.GetNotifications;

public record GetNotificationsQuery(Guid UserId, bool UnreadOnly, int Page, int PageSize) : IRequest<GetNotificationsResult>;

public record NotificationDto(Guid Id, string Type, string DataJson, bool IsRead, DateTime CreatedAtUtc);

public record GetNotificationsResult(List<NotificationDto> Items, int Page, int PageSize, int TotalCount);