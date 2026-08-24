using TelehealthPlatform.Domain.Common;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Mirrors RefreshToken's TokenHash pattern exactly (Security Deep-Dive §2.2).
/// 1-hour expiry, single-use via IsUsed.
/// </summary>
public class PasswordResetToken : Entity
{
    public Guid UserId { get; init; }
    public string TokenHash { get; init; } = default!;
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UsedAtUtc { get; private set; }
    public string? RequestIpAddress { get; init; }
    public string? UserAgent { get; init; }
    public bool IsUsed { get; private set; }

    private PasswordResetToken() { }

    public static PasswordResetToken Create(Guid userId, string tokenHash, string? ipAddress, string? userAgent, DateTime nowUtc)
    {
        return new PasswordResetToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAtUtc = nowUtc.AddHours(1),
            CreatedAtUtc = nowUtc,
            RequestIpAddress = ipAddress,
            UserAgent = userAgent
        };
    }

    public bool IsValid(DateTime nowUtc) => !IsUsed && ExpiresAtUtc > nowUtc;

    public void MarkUsed(DateTime nowUtc)
    {
        IsUsed = true;
        UsedAtUtc = nowUtc;
    }
}