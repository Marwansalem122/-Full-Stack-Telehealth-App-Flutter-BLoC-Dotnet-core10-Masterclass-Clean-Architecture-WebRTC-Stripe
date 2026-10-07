using MediatR;

namespace TelehealthPlatform.Application.Auth.Logout;

public record LogoutCommand(string RefreshToken) : IRequest;