using TelehealthPlatform.Domain.Entities;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Common.Interfaces;

public interface IUserDeviceTokenRepository
{
    Task<UserDeviceToken?> GetByTokenAsync(string token, CancellationToken ct);
    Task<List<UserDeviceToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct);
    Task AddAsync(UserDeviceToken deviceToken, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);


    /// <summary>Cleanup job query (ERD: LastUsedAt < Now.AddMonths(-3)) —
    /// bulk delete, same ExecuteUpdate/Delete pattern as RefreshToken's
    /// bulk revoke, not a load-then-loop.</summary>
    Task<int> DeleteStaleTokensAsync(DateTime staleBeforeUtc, CancellationToken ct);
}