using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Auth.Logout;

/// <summary>Idempotent from the client's perspective (API Contract v2) —
/// 204 regardless of whether the token was found/already revoked.</summary>
public class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    ITokenHasher tokenHasher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        var hash = tokenHasher.Hash(request.RefreshToken);
        var token = await refreshTokenRepository.GetByTokenHashAsync(hash, ct);

        if (token is not null && token.RevokedAtUtc is null)
        {
            token.Revoke(RefreshTokenRevocationReason.UserLogout, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}