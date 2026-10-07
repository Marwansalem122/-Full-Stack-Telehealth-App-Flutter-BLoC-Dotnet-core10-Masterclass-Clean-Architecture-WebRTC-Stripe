using MediatR;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Notifications.CreateNotification;

/// <summary>
/// INTERNAL-USE ONLY — sent via IMediator from OTHER handlers (payment
/// confirmation, AI summary completion, etc.), never exposed as a public
/// HTTP endpoint. Centralizes "persist first, notify second" in one place
/// instead of every handler duplicating the sequence.
/// </summary>
public record CreateNotificationCommand(
    Guid UserId,
    NotificationType Type,
    string DataJson,
    string PushTitle,
    string PushBody) : IRequest;