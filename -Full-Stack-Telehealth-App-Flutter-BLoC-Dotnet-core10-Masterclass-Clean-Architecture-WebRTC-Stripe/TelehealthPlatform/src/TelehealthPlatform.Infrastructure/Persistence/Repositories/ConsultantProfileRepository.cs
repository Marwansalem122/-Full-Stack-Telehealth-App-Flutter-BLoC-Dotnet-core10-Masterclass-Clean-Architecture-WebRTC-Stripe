

using Microsoft.EntityFrameworkCore;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Infrastructure.Persistence.Repositories
{
    public class ConsultantProfileRepository(TelehealthDbContext db) : IConsultantProfileRepository
    {
        public Task<ConsultantProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct)
            => db.ConsultantProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

        public async Task AddAsync(ConsultantProfile profile, CancellationToken ct) => await db.ConsultantProfiles.AddAsync(profile, ct);
    }
}
