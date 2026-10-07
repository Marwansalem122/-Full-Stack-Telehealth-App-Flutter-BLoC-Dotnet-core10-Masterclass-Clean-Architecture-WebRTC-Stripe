using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Updated per Security Deep-Dive §3: EmailConfirmed gates booking/payment
/// (Requirements §3.5). No separate verification-tokens table in v1 —
/// a new registration overwrites the old token (§3.2 rationale).
/// </summary>
public class User : Entity
{
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; init; } // immutable — Requirements §3.1
    public DateTime CreatedAtUtc { get; init; }

    public bool EmailConfirmed { get; private set; }
    public string? EmailVerificationTokenHash { get; private set; }
    public DateTime? EmailVerificationSentAtUtc { get; private set; }

    public ConsultantProfile? ConsultantProfile { get; private set; }
    public PatientProfile? PatientProfile { get; private set; }

    private User() { }

    public static User Register(string name, string email, string passwordHash, UserRole role,
        string emailVerificationTokenHash, DateTime nowUtc)
    {
        return new User
        {
            Name = name,
            Email = email,
            PasswordHash = passwordHash,
            Role = role,
            CreatedAtUtc = nowUtc,
            EmailConfirmed = false,
            EmailVerificationTokenHash = emailVerificationTokenHash,
            EmailVerificationSentAtUtc = nowUtc
        };
    }

    /// <summary>Called after comparing the incoming raw token's hash against
    /// EmailVerificationTokenHash (Security Deep-Dive §3.3) — comparison
    /// itself happens in the Application layer, not here.</summary>
    public void ConfirmEmail()
    {
        EmailConfirmed = true;
        EmailVerificationTokenHash = null; // burn the token — single use
    }

    /// <summary>For /auth/resend-verification.</summary>
    public void SetNewEmailVerificationToken(string tokenHash, DateTime nowUtc)
    {
        EmailVerificationTokenHash = tokenHash;
        EmailVerificationSentAtUtc = nowUtc;
    }

    /// <summary>Used by both /auth/change-password and /auth/reset-password —
    /// caller is responsible for revoking RefreshTokens in the same
    /// transaction (Security Deep-Dive §8.4, decision #3).</summary>
    public void ChangePasswordHash(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
    }
}