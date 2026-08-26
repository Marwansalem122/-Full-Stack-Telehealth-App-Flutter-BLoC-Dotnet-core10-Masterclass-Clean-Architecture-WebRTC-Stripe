using MediatR;

namespace TelehealthPlatform.Application.Auth.Login;

public record LoginCommand(string Email, string Password, string? IpAddress, string? UserAgent) : IRequest<LoginResult>;

public record LoginResult(string AccessToken, string RefreshToken, int ExpiresIn, bool EmailConfirmed);