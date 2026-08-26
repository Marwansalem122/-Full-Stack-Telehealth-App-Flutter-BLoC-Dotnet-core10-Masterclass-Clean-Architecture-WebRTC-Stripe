using MediatR;

namespace TelehealthPlatform.Application.Auth.GetPasswordPolicy;

public record GetPasswordPolicyQuery : IRequest<PasswordPolicyResult>;

public record PasswordPolicyResult(
    int MinLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialCharacter);