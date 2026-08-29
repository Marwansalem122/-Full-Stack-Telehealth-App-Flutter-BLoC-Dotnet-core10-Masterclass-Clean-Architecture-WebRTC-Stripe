using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Repositories;

public class FailedLoginAttemptRepository(TelehealthDbContext db) : IFailedLoginAttemptRepository
{
    public async Task RecordAsync(string? email, string? ipAddress, string? userAgent, bool passwordValid, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var attempt = FailedLoginAttempt.Record(email ?? string.Empty, ipAddress, userAgent, passwordValid, nowUtc);
        await db.FailedLoginAttempts.AddAsync(attempt, cancellationToken);
        // Deliberately saves immediately rather than waiting for the
        // caller's SaveChangesAsync — audit records should persist even if
        // the surrounding login attempt subsequently throws.
        await db.SaveChangesAsync(cancellationToken);
    }
}