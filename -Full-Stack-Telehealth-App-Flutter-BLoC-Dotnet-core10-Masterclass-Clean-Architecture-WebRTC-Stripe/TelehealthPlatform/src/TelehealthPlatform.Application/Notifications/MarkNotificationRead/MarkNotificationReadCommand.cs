using MediatR;

namespace TelehealthPlatform.Application.Notifications.MarkNotificationRead;

/// <summary>UserId comes from JWT claims — the handler verifies resource
/// ownership (this notification belongs to the caller), not just that
/// SOME notification with this Id exists.</summary>
public record MarkNotificationReadCommand(Guid NotificationId, Guid UserId) : IRequest;