namespace TelehealthPlatform.Application.Common.Interfaces;

/// <summary>Same pattern as IEmailService — Application knows "push this
/// notification," never Firebase specifics.</summary>
public interface IPushNotificationService
{
    /// <summary>Sends to every active device token for this user. Silently
    /// no-ops if the user has zero registered devices — push is best-effort
    /// delivery, never the source of truth (the Notification row is already
    /// persisted by the time this is called).</summary>
    Task SendAsync(Guid userId, string title, string body, string dataJson, CancellationToken ct);
}