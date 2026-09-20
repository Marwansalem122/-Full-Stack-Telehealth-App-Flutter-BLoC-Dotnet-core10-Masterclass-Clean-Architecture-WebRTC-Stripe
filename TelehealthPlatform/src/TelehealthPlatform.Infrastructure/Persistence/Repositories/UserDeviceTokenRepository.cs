using Microsoft.EntityFrameworkCore;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Repositories;

public class UserDeviceTokenRepository(TelehealthDbContext db) : IUserDeviceTokenRepository
{
    public Task<UserDeviceToken?> GetByTokenAsync(string token, CancellationToken ct)
        => db.UserDeviceTokens.FirstOrDefaultAsync(t => t.Token == token, ct);

    public Task<List<UserDeviceToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct)
        => db.UserDeviceTokens.Where(t => t.UserId == userId).ToListAsync(ct);

    public async Task AddAsync(UserDeviceToken deviceToken, CancellationToken ct)
        => await db.UserDeviceTokens.AddAsync(deviceToken, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.UserDeviceTokens.FindAsync([id], ct);
        if (entity is not null) db.UserDeviceTokens.Remove(entity);
    }

    public Task<int> DeleteStaleTokensAsync(DateTime staleBeforeUtc, CancellationToken ct)
        => db.UserDeviceTokens
            .Where(t => t.LastUsedAtUtc < staleBeforeUtc)
            .ExecuteDeleteAsync(ct);
}