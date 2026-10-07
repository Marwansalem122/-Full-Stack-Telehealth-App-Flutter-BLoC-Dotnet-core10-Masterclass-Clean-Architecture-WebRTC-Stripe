using MediatR;

namespace TelehealthPlatform.Application.Auth.RefreshToken;

public record RefreshTokenCommand(string RefreshToken, string? IpAddress, string? UserAgent) : IRequest<RefreshTokenResult>;

public record RefreshTokenResult(string AccessToken, string RefreshToken, int ExpiresIn);