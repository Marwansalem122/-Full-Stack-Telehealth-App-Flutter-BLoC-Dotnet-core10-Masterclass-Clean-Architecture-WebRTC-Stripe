using FluentValidation;
using TelehealthPlatform.Application.Common;

namespace TelehealthPlatform.Application.Auth.Register;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).SetValidator(new PasswordRuleValidator());
        RuleFor(x => x.Role).Must(r => r is "Patient" or "Consultant")
            .WithMessage("Role must be 'Patient' or 'Consultant'.");
    }
}