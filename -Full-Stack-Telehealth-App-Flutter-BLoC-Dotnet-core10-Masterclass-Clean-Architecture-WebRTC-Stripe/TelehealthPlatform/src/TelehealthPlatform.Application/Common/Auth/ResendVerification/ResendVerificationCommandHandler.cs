using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Application.Auth.ResendVerification;

/// <summary>
/// Same anti-enumeration silence pattern as ForgotPassword — always
/// succeeds from the caller's perspective. Overwrites the OLD verification
/// token entirely (User.SetNewEmailVerificationToken) rather than keeping
/// both valid — a resend should invalidate the previous email link, not
/// create a second valid one (Requirements: "a new registration overwrites
/// the old token").
/// </summary>
public class ResendVerificationCommandHandler(
    IUserRepository userRepository,
    ITokenHasher tokenHasher,
    ISecureTokenGenerator secureTokenGenerator,
    IEmailService emailService,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<ResendVerificationCommand>
{
    public async Task Handle(ResendVerificationCommand request, CancellationToken ct)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, ct);

        // Silent on "doesn't exist" AND "already verified" — no reason to
        // resend a verification email to an already-confirmed account,
        // and no reason to tell the caller which case applied.
        if (user is null || user.EmailConfirmed)
            return;

        var rawToken = secureTokenGenerator.Generate();
        var tokenHash = tokenHasher.Hash(rawToken);

        user.SetNewEmailVerificationToken(tokenHash, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        await emailService.SendEmailVerificationAsync(user.Email, rawToken, ct);
    }
}