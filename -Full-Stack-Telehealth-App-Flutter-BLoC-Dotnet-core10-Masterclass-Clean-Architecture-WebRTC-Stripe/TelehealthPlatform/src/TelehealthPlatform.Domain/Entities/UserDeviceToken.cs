using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Multiple devices per user supported (phone + tablet). Token is UNIQUE
/// across all users — a device token can't legitimately belong to two
/// accounts simultaneously (ERD v1.5).
/// </summary>
public class UserDeviceToken : Entity
{
    public Guid UserId { get; init; }
    public string Token { get; private set; } = default!;
    public DevicePlatform Platform { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime LastUsedAtUtc { get; private set; }

    private UserDeviceToken() { }

    public static UserDeviceToken Register(Guid userId, string token, DevicePlatform platform, DateTime nowUtc)
    {
        return new UserDeviceToken
        {
            UserId = userId,
            Token = token,
            Platform = platform,
            CreatedAtUtc = nowUtc,
            LastUsedAtUtc = nowUtc
        };
    }

    /// <summary>Called on successful FCM delivery — feeds the staleness
    /// cleanup job (LastUsedAt < Now - 3 months, per ERD).</summary>
    public void MarkUsed(DateTime nowUtc) => LastUsedAtUtc = nowUtc;

    /// <summary>Handles onTokenRefresh from Flutter — replaces the token
    /// value in place rather than deleting+recreating the row, preserving
    /// CreatedAtUtc history for this device.</summary>
    public void ReplaceToken(string newToken, DateTime nowUtc)
    {
        Token = newToken;
        LastUsedAtUtc = nowUtc;
    }
}