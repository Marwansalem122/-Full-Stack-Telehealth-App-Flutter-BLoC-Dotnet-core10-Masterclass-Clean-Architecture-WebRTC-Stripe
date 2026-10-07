using Microsoft.Extensions.Logging;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Infrastructure.Email;

/// <summary>
/// TEMPORARY stub — logs the link instead of sending a real email, so you
/// can test the full Auth flow (copy the link from the console/logs) before
/// wiring up a real provider (SendGrid/SES/etc). Swap the DI registration
/// in Program.cs for a real implementation before this touches production.
/// </summary>
public class ConsoleEmailService(ILogger<ConsoleEmailService> logger) : IEmailService
{
    public Task SendEmailVerificationAsync(string toEmail, string rawVerificationToken, CancellationToken ct)
    {
        logger.LogWarning("[STUB EMAIL] Verification link for {Email}: https://app.telehealth.example.com/verify-email?token={Token}",
            toEmail, rawVerificationToken);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string toEmail, string rawResetToken, CancellationToken ct)
    {
        logger.LogWarning("[STUB EMAIL] Password reset link for {Email}: https://app.telehealth.example.com/reset-password?token={Token}",
            toEmail, rawResetToken);
        return Task.CompletedTask;
    }
}