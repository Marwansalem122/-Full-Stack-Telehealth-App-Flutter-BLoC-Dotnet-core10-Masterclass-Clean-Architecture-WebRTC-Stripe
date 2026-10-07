using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Common.Interfaces;

public interface IPatientProfileRepository
{
    Task<PatientProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task AddAsync(PatientProfile profile, CancellationToken ct);
}