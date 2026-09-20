using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Notifications.CreateNotification;

/// <summary>
/// The canonical "persist first, notify second" implementation (Sequence
/// Diagrams cross-cutting pattern). The Notification row commits BEFORE
/// any delivery is attempted. SignalR and push are both best-effort,
/// independent of each other and of the already-durable record — if
/// either fails, the notification still exists via GET /notifications.
/// </summary>
public class CreateNotificationCommandHandler(
    INotificationRepository notificationRepository,
    IPushNotificationService pushNotificationService,
    INotificationBroadcaster notificationBroadcaster,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateNotificationCommand>
{
    public async Task Handle(CreateNotificationCommand request, CancellationToken ct)
    {
        var notification = Notification.Create(request.UserId, request.Type, request.DataJson);

        await notificationRepository.AddAsync(notification, ct);
        await unitOfWork.SaveChangesAsync(ct); // <-- commit BEFORE any delivery attempt

        await notificationBroadcaster.BroadcastAsync(notification, ct);
        await pushNotificationService.SendAsync(request.UserId, request.PushTitle, request.PushBody, request.DataJson, ct);
    }
}