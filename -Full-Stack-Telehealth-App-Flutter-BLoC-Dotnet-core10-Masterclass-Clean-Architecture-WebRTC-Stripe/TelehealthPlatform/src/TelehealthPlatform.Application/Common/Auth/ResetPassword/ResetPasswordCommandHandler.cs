using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Auth.ResetPassword;

/// <summary>
/// Single-use enforcement (PasswordResetToken.IsUsed) + revokes ALL of the
/// user's active refresh tokens on successful reset (Security Deep-Dive
/// §8.4, decision #3: "a password change should invalidate every existing
/// session" — if someone's password was compromised, their old sessions
/// shouldn't survive the reset).
/// </summary>
public class ResetPasswordCommandHandler(
    IPasswordResetTokenRepository passwordResetTokenRepository,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenHasher tokenHasher,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        var tokenHash = tokenHasher.Hash(request.RawToken);
        var resetToken = await passwordResetTokenRepository.GetByTokenHashAsync(tokenHash, ct)
            ?? throw new InvalidOperationException("Invalid or expired reset link.");

        if (!resetToken.IsValid(clock.UtcNow))
            throw new InvalidOperationException("Invalid or expired reset link.");

        var user = await userRepository.GetByIdAsync(resetToken.UserId, ct)
            ?? throw new InvalidOperationException("Invalid or expired reset link.");

        var newPasswordHash = passwordHasher.Hash(request.NewPassword);
        user.ChangePasswordHash(newPasswordHash);

        resetToken.MarkUsed(clock.UtcNow);

        // Revoke every active refresh token for this user — password reset
        // invalidates all existing sessions (Security Deep-Dive §8.4).
        // Note: this needs a "revoke all for user" repository method,
        // distinct from RevokeFamilyAsync (which targets ONE family).
        await refreshTokenRepository.RevokeAllForUserAsync(user.Id, RefreshTokenRevocationReason.PasswordChanged, clock.UtcNow, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }
}