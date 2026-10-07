namespace TelehealthPlatform.Application.Common.Interfaces;

/// <summary>Infrastructure implements this against whatever provider you
/// pick (SendGrid, SES, etc.) — Application only knows "send this kind of
/// email," never the transport.</summary>
public interface IEmailService
{
    Task SendEmailVerificationAsync(string toEmail, string rawVerificationToken, CancellationToken ct);
    Task SendPasswordResetAsync(string toEmail, string rawResetToken, CancellationToken ct);
}