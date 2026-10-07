using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Common.Interfaces;

public interface IPasswordResetTokenRepository
{
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task AddAsync(PasswordResetToken token, CancellationToken ct);
}