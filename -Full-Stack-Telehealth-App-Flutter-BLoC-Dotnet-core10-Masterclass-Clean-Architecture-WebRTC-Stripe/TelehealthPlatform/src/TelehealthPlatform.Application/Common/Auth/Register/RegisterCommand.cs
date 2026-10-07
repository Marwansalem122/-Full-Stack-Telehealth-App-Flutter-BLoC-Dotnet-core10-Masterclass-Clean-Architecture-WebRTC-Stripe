using MediatR;

namespace TelehealthPlatform.Application.Auth.Register;

public record RegisterCommand(string Name, string Email, string Password, string Role) : IRequest<RegisterResult>;

public record RegisterResult(Guid UserId, string Role);