using MediatR;

namespace TelehealthPlatform.Application.Auth.ConfirmEmail;

public record ConfirmEmailCommand(string RawToken) : IRequest;