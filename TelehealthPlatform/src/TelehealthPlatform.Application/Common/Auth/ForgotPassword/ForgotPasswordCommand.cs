using MediatR;

namespace TelehealthPlatform.Application.Auth.ForgotPassword;

public record ForgotPasswordCommand(string Email, string? IpAddress, string? UserAgent) : IRequest;