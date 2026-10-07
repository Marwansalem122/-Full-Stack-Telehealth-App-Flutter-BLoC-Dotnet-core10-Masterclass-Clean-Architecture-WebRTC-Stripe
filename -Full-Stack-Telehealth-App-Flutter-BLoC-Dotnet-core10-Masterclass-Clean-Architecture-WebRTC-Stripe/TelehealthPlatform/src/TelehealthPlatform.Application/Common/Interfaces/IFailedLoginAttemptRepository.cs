namespace TelehealthPlatform.Application.Common.Interfaces;

public interface IFailedLoginAttemptRepository
{
    Task RecordAsync(
        string? email,
        string? ipAddress,
        string? userAgent,
        bool passwordValid,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}