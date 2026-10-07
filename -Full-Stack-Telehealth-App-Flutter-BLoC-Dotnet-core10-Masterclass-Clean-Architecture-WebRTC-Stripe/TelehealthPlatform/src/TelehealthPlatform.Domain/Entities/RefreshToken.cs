using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Updated per Security Deep-Dive §6: FamilyId groups every token produced
/// by rotation from one original login. Reuse of an already-revoked token
/// is the "nuclear option" trigger — the Application layer must revoke
/// every token sharing this FamilyId, not just this one (§6.6, decision #2).
/// </summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; init; }
    public string TokenHash { get; private set; } = default!;
    public Guid FamilyId { get; init; }
    public Guid? ReplacedByTokenId { get; private set; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; private set; }
    public RefreshTokenRevocationReason? RevocationReason { get; private set; }
    public string? RequestIpAddress { get; init; }
    public string? UserAgent { get; init; }

    private RefreshToken() { }

    /// <summary>New login — starts a brand new family.</summary>
    public static RefreshToken CreateNewFamily(Guid userId, string tokenHash, DateTime expiresAtUtc,
        string? ipAddress, string? userAgent, DateTime nowUtc)
    {
        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = Guid.NewGuid(),
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = nowUtc,
            RequestIpAddress = ipAddress,
            UserAgent = userAgent
        };
    }

    /// <summary>Rotation — same family, new token (Security Deep-Dive §6.6:
    /// rotation is mandatory on every refresh).</summary>
    public static RefreshToken CreateRotated(RefreshToken previous, string newTokenHash, DateTime expiresAtUtc,
        string? ipAddress, string? userAgent, DateTime nowUtc)
    {
        return new RefreshToken
        {
            UserId = previous.UserId,
            TokenHash = newTokenHash,
            FamilyId = previous.FamilyId, // same family
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = nowUtc,
            RequestIpAddress = ipAddress,
            UserAgent = userAgent
        };
    }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    public void Revoke(RefreshTokenRevocationReason reason, DateTime nowUtc, Guid? replacedByTokenId = null)
    {
        RevokedAtUtc = nowUtc;
        RevocationReason = reason;
        ReplacedByTokenId = replacedByTokenId;
    }
}