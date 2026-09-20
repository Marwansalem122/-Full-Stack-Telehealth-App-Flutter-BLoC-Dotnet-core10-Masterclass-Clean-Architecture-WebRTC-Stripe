using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Infrastructure.Notifications;

/// <summary>
/// Requires the FirebaseAdmin NuGet package + a service account JSON
/// credential configured at startup (FirebaseApp.Create in Program.cs —
/// added when we build the API layer). A token FCM reports as
/// unregistered/invalid is deleted immediately rather than waiting for the
/// 3-month staleness cleanup job.
/// </summary>
public class FirebasePushNotificationService(
    IUserDeviceTokenRepository deviceTokenRepository,
    ILogger<FirebasePushNotificationService> logger) : IPushNotificationService
{
    public async Task SendAsync(Guid userId, string title, string body, string dataJson, CancellationToken ct)
    {
        var devices = await deviceTokenRepository.GetActiveByUserIdAsync(userId, ct);
        if (devices.Count == 0) return; // no registered devices — not an error, push is best-effort

        foreach (var device in devices)
        {
            var message = new Message
            {
                Token = device.Token,
                Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = body },
                Data = new Dictionary<string, string> { ["payload"] = dataJson }
            };

            try
            {
                await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
                device.MarkUsed(DateTime.UtcNow);
            }
            catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
            {
                await deviceTokenRepository.DeleteAsync(device.Id, ct);
                logger.LogInformation("Removed stale FCM token for user {UserId}", userId);
            }
        }
    }
}