using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Auth.ChangePassword;

/// <summary>
/// Different trust model from ResetPassword: the caller already proved
/// identity via an active JWT, but this action is sensitive enough to
/// re-verify the CURRENT password before allowing a change (defends
/// against a hijacked/left-open session). Same "revoke everything" rule
/// as ResetPassword applies here too (Security Deep-Dive §8.4).
/// </summary>
public class ChangePasswordCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, ct)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        var newPasswordHash = passwordHasher.Hash(request.NewPassword);
        user.ChangePasswordHash(newPasswordHash);

        // Same rationale as ResetPassword: invalidate every existing
        // session, including the one making this request — the client
        // will need to log in again with the new password.
        await refreshTokenRepository.RevokeAllForUserAsync(user.Id, RefreshTokenRevocationReason.PasswordChanged, clock.UtcNow, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }
}