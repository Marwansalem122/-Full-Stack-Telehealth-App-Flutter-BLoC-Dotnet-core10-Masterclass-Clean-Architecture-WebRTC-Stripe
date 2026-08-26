using FluentValidation;
using TelehealthPlatform.Application.Common;

namespace TelehealthPlatform.Application.Auth.ChangePassword;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).SetValidator(new PasswordRuleValidator());
        RuleFor(x => x)
            .Must(x => x.CurrentPassword != x.NewPassword)
            .WithMessage("New password must be different from the current password.");
    }
}