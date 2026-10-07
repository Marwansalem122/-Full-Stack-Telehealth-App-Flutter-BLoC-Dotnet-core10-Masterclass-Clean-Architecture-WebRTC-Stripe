using TelehealthPlatform.Domain.Common;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Audit-only (Security Deep-Dive §7.5) — NOT used for lockout logic itself,
/// that's ASP.NET Core Identity's built-in AccessFailedCount. This table
/// exists purely for forensics / future v2 alerting.
/// </summary>
public class FailedLoginAttempt : Entity
{
    public string EmailAttempted { get; init; } = default!;
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public DateTime AttemptedAtUtc { get; init; }
    public bool WasSuccessful { get; init; }

    private FailedLoginAttempt() { }

    public static FailedLoginAttempt Record(string emailAttempted, string? ipAddress, string? userAgent, bool wasSuccessful, DateTime nowUtc)
    {
        return new FailedLoginAttempt
        {
            EmailAttempted = emailAttempted,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AttemptedAtUtc = nowUtc,
            WasSuccessful = wasSuccessful
        };
    }
}