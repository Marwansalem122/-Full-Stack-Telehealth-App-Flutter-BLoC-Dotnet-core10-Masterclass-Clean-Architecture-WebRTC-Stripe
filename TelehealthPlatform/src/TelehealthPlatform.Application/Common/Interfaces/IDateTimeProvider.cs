namespace TelehealthPlatform.Application.Common.Interfaces;

/// <summary>Injected everywhere "now" matters (Appointment.Cancel's 24h
/// check, token expiry, etc.) so handlers stay unit-testable without
/// mocking DateTime.UtcNow directly.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}