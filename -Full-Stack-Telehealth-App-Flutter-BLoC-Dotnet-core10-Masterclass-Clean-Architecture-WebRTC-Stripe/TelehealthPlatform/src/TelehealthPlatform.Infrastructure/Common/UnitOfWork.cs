using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Infrastructure.Persistence;

namespace TelehealthPlatform.Infrastructure.Common;

public class UnitOfWork(TelehealthDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}