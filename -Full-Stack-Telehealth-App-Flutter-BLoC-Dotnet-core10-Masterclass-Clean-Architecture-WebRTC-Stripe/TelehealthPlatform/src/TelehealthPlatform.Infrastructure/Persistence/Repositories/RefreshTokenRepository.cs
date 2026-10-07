using Microsoft.EntityFrameworkCore;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository(TelehealthDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct)
        => db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct) => await db.RefreshTokens.AddAsync(token, ct);

    public Task RevokeFamilyAsync(Guid familyId, RefreshTokenRevocationReason reason, DateTime nowUtc, CancellationToken ct)
    {
        return db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.RevokedAtUtc, nowUtc)
                .SetProperty(t => t.RevocationReason, reason), ct);
    }

    public Task RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, DateTime nowUtc, CancellationToken ct)
    {
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.RevokedAtUtc, nowUtc)
                .SetProperty(t => t.RevocationReason, reason), ct);
    }
}