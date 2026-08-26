using TelehealthPlatform.Domain.Entities;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Common.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task AddAsync(RefreshToken token, CancellationToken ct);

    /// <summary>Bulk-revokes every active token sharing this FamilyId in ONE
    /// round-trip — reuse detection (Security Deep-Dive §6.6).</summary>
    Task RevokeFamilyAsync(Guid familyId, RefreshTokenRevocationReason reason, DateTime nowUtc, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, DateTime nowUtc, CancellationToken ct);
}