using MediatR;

namespace TelehealthPlatform.Application.Notifications.RegisterDeviceToken;

public record RegisterDeviceTokenCommand(Guid UserId, string Token, string Platform) : IRequest;