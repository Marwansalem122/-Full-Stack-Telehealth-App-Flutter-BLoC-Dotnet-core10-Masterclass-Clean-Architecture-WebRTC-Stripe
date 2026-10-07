using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Application.Notifications.MarkNotificationRead;

public class MarkNotificationReadCommandHandler(
    INotificationRepository notificationRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<MarkNotificationReadCommand>
{
    public async Task Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        var notification = await notificationRepository.GetByIdAsync(request.NotificationId, ct)
            ?? throw new KeyNotFoundException("Notification not found.");

        if (notification.UserId != request.UserId)
            throw new UnauthorizedAccessException("Not authorized to modify this notification.");

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}