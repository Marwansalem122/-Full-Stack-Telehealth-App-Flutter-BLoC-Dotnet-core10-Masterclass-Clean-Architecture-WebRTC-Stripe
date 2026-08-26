using MediatR;

namespace TelehealthPlatform.Application.Auth.ResendVerification;

public record ResendVerificationCommand(string Email) : IRequest;