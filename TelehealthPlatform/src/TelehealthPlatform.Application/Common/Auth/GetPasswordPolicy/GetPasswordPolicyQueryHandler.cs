using MediatR;
using TelehealthPlatform.Application.Common;

namespace TelehealthPlatform.Application.Auth.GetPasswordPolicy;

public class GetPasswordPolicyQueryHandler : IRequestHandler<GetPasswordPolicyQuery, PasswordPolicyResult>
{
    public Task<PasswordPolicyResult> Handle(GetPasswordPolicyQuery request, CancellationToken ct)
    {
        var result = new PasswordPolicyResult(
            MinLength: PasswordPolicyConstants.MinLength,
            RequireUppercase: PasswordPolicyConstants.RequireUppercase,
            RequireLowercase: PasswordPolicyConstants.RequireLowercase,
            RequireDigit: PasswordPolicyConstants.RequireDigit,
            RequireSpecialCharacter: PasswordPolicyConstants.RequireSpecialCharacter);

        return Task.FromResult(result);
    }
}