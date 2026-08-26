using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Common.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task<User?> GetByEmailVerificationTokenHashAsync(string tokenHash, CancellationToken ct);
}