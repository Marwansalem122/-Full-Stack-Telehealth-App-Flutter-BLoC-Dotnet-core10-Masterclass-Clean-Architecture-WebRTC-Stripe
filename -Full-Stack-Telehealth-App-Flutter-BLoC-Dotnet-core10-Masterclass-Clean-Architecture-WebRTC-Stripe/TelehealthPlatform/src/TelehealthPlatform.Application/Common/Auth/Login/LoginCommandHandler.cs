using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Application.Auth.Login;

/// <summary>
/// Deliberately does NOT block login when EmailConfirmed = false — the
/// Security Deep-Dive gates BOOKING/PAYMENT on email confirmation, not
/// authentication itself. LoginResult surfaces the flag so Flutter can
/// show a "please verify your email" banner without preventing basic
/// access. Records every attempt to FailedLoginAttempts regardless of
/// outcome (audit-only — lockout logic itself is ASP.NET Core Identity's
/// job, not this handler's).
/// </summary>
public class LoginCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    ITokenHasher tokenHasher,
    ISecureTokenGenerator secureTokenGenerator,
    IRefreshTokenRepository refreshTokenRepository,
    IFailedLoginAttemptRepository failedLoginRepository, // define analogous to IUserRepository
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, ct);
        var passwordValid = user is not null && passwordHasher.Verify(request.Password, user.PasswordHash);

        await failedLoginRepository.RecordAsync(request.Email, request.IpAddress, request.UserAgent, passwordValid, clock.UtcNow, ct);

        if (!passwordValid)
            throw new UnauthorizedAccessException("Invalid email or password."); // deliberately generic — don't reveal which field was wrong

        var (accessToken, expiresAtUtc) = jwtTokenGenerator.GenerateAccessToken(user!);

        var rawRefreshToken = secureTokenGenerator.Generate();
        var refreshTokenHash = tokenHasher.Hash(rawRefreshToken);
        var refreshTokenExpiry = clock.UtcNow.AddDays(30); // adjust to your documented refresh-token lifetime

        var refreshToken = Domain.Entities.RefreshToken.CreateNewFamily(
            user!.Id, refreshTokenHash, refreshTokenExpiry, request.IpAddress, request.UserAgent, clock.UtcNow);

        await refreshTokenRepository.AddAsync(refreshToken, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new LoginResult(
            accessToken,
            rawRefreshToken, // raw goes to the client — only the hash is ever persisted
            (int)(expiresAtUtc - clock.UtcNow).TotalSeconds,
            user.EmailConfirmed);
    }
}