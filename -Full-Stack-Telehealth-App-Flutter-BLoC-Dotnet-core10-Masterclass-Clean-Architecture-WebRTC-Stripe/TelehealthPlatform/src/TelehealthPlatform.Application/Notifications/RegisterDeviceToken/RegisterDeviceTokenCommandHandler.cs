using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Notifications.RegisterDeviceToken;

/// <summary>
/// Handles three cases explicitly: brand-new token, the same user
/// re-registering (refresh/heartbeat), or the token previously belonging
/// to a DIFFERENT user (same physical device, different account). The
/// third case deletes-and-recreates rather than mutating UserId — that
/// field is `init` by design on UserDeviceToken, so reassignment can't be
/// a simple property update.
/// </summary>
public class RegisterDeviceTokenCommandHandler(
    IUserDeviceTokenRepository deviceTokenRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<RegisterDeviceTokenCommand>
{
    public async Task Handle(RegisterDeviceTokenCommand request, CancellationToken ct)
    {
        var platform = Enum.Parse<DevicePlatform>(request.Platform, ignoreCase: true);
        var existing = await deviceTokenRepository.GetByTokenAsync(request.Token, ct);

        if (existing is null)
        {
            var deviceToken = UserDeviceToken.Register(request.UserId, request.Token, platform, clock.UtcNow);
            await deviceTokenRepository.AddAsync(deviceToken, ct);
        }
        else if (existing.UserId == request.UserId)
        {
            existing.MarkUsed(clock.UtcNow);
        }
        else
        {
            await deviceTokenRepository.DeleteAsync(existing.Id, ct);
            var reassigned = UserDeviceToken.Register(request.UserId, request.Token, platform, clock.UtcNow);
            await deviceTokenRepository.AddAsync(reassigned, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}