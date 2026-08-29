using Microsoft.EntityFrameworkCore;

using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Repositories
{
    public class PatientProfileRepository(TelehealthDbContext db) : IPatientProfileRepository
    {
        public Task<PatientProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct)
            => db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

        public async Task AddAsync(PatientProfile profile, CancellationToken ct) => await db.PatientProfiles.AddAsync(profile, ct);
    }
}
