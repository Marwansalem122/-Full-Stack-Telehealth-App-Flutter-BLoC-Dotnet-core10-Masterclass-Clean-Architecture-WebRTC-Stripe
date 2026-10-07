using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Auth.ForgotPassword;

/// <summary>
/// Anti-enumeration by design (Security Deep-Dive / API Contract note under
/// /auth/register): ALWAYS returns success to the caller, regardless of
/// whether the email exists, is unverified, or anything else. The actual
/// email is only sent when every condition is met — the silence itself is
/// the security property, not an accident.
/// </summary>
public class ForgotPasswordCommandHandler(
    IUserRepository userRepository,
    IPasswordResetTokenRepository passwordResetTokenRepository,
    ITokenHasher tokenHasher,
    ISecureTokenGenerator secureTokenGenerator,
    IEmailService emailService,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, ct);

        // Deliberately silent on both "doesn't exist" and "unverified" —
        // API Contract: "will not send a reset email to unverified
        // addresses (but still returns the same 200 response)."
        if (user is null || !user.EmailConfirmed)
            return;

        var rawToken = secureTokenGenerator.Generate();
        var tokenHash = tokenHasher.Hash(rawToken);

        var resetToken = PasswordResetToken.Create(user.Id, tokenHash, request.IpAddress, request.UserAgent, clock.UtcNow);

        await passwordResetTokenRepository.AddAsync(resetToken, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await emailService.SendPasswordResetAsync(user.Email, rawToken, ct);
    }
}