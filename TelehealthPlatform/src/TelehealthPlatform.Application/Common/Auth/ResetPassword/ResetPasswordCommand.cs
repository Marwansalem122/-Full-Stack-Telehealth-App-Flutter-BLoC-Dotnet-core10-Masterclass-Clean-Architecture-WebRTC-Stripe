using MediatR;

namespace TelehealthPlatform.Application.Auth.ResetPassword;

public record ResetPasswordCommand(string RawToken, string NewPassword) : IRequest;