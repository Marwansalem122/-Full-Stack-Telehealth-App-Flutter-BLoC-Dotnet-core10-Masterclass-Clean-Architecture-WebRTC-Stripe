using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Auth.RefreshToken;

/// <summary>
/// Implements rotation + reuse detection exactly as designed (Security
/// Deep-Dive §6.6): every refresh both revokes the old token AND issues a
/// new one in the same family. If the incoming token is found but already
/// revoked, that's theft/reuse — the entire family gets nuked, not just
/// this one token.
/// </summary>
public class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IUserRepository userRepository,
    ITokenHasher tokenHasher,
    ISecureTokenGenerator secureTokenGenerator,
    IJwtTokenGenerator jwtTokenGenerator,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<RefreshTokenCommand, RefreshTokenResult>
{
    public async Task<RefreshTokenResult> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var incomingHash = tokenHasher.Hash(request.RefreshToken);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(incomingHash, ct);

        if (existingToken is null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        if (existingToken.RevokedAtUtc is not null)
        {
            // Reuse of an already-revoked token — nuclear option.
            await refreshTokenRepository.RevokeFamilyAsync(
                existingToken.FamilyId, RefreshTokenRevocationReason.ReuseDetected, clock.UtcNow, ct);
            await unitOfWork.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        if (!existingToken.IsActive(clock.UtcNow))
            throw new UnauthorizedAccessException("Refresh token expired.");

        var user = await userRepository.GetByIdAsync(existingToken.UserId, ct)
            ?? throw new UnauthorizedAccessException("User not found.");

        var rawNewRefreshToken = secureTokenGenerator.Generate();
        var newTokenHash = tokenHasher.Hash(rawNewRefreshToken);
        var newExpiry = clock.UtcNow.AddDays(30);

        var rotatedToken = Domain.Entities.RefreshToken.CreateRotated(
            existingToken, newTokenHash, newExpiry, request.IpAddress, request.UserAgent, clock.UtcNow);

        existingToken.Revoke(RefreshTokenRevocationReason.ReplacedByRotation, clock.UtcNow, rotatedToken.Id);

        await refreshTokenRepository.AddAsync(rotatedToken, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var (accessToken, expiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(user);

        return new RefreshTokenResult(accessToken, rawNewRefreshToken, (int)(expiresAtUtc - clock.UtcNow).TotalSeconds);
    }
}