using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Application.Auth.ConfirmEmail;

/// <summary>
/// Hashes the incoming raw token and compares against
/// User.EmailVerificationTokenHash — same pattern as every other token
/// in this system (never compare/store raw). Deliberately throws a
/// generic failure for "not found" AND "expired" — no reason to
/// distinguish them for the caller (Security Deep-Dive §3.3).
/// </summary>
public class ConfirmEmailCommandHandler(
    IUserRepository userRepository,
    ITokenHasher tokenHasher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<ConfirmEmailCommand>
{
    private const int TokenValidityHours = 24; // adjust to match your documented expiry

    public async Task Handle(ConfirmEmailCommand request, CancellationToken ct)
    {
        var tokenHash = tokenHasher.Hash(request.RawToken);
        var user = await userRepository.GetByEmailVerificationTokenHashAsync(tokenHash, ct)
            ?? throw new InvalidOperationException("Invalid or expired verification link.");

        if (user.EmailVerificationSentAtUtc is null
            || clock.UtcNow > user.EmailVerificationSentAtUtc.Value.AddHours(TokenValidityHours))
            throw new InvalidOperationException("Invalid or expired verification link.");

        user.ConfirmEmail();
        await unitOfWork.SaveChangesAsync(ct);
    }
}