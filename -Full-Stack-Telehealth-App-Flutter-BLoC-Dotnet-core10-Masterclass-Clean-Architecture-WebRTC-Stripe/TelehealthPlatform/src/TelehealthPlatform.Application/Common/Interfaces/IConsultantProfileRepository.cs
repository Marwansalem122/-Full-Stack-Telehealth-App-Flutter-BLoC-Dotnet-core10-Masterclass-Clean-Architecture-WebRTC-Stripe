using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Common.Interfaces;

public interface IConsultantProfileRepository
{
    Task<ConsultantProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task AddAsync(ConsultantProfile profile, CancellationToken ct);
}