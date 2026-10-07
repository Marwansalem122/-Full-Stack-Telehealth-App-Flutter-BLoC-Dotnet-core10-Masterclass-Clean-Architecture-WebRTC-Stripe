using FluentValidation;
using FluentValidation.Validators;

namespace TelehealthPlatform.Application.Common;

/// <summary>
/// Shared child-validator so RegisterCommandValidator and
/// ChangePasswordCommandValidator both enforce EXACTLY the same rules,
/// read from PasswordPolicyConstants — this is what actually closes the
/// drift risk, not just documenting the constant separately.
/// </summary>
public class PasswordRuleValidator : AbstractValidator<string>
{
    public PasswordRuleValidator()
    {
        RuleFor(password => password)
            .NotEmpty()
            .MinimumLength(PasswordPolicyConstants.MinLength)
                .WithMessage($"Password must be at least {PasswordPolicyConstants.MinLength} characters.")
            .Must(HaveUppercaseIfRequired)
                .WithMessage("Password must contain an uppercase letter.")
            .Must(HaveLowercaseIfRequired)
                .WithMessage("Password must contain a lowercase letter.")
            .Must(HaveDigitIfRequired)
                .WithMessage("Password must contain a digit.")
            .Must(HaveSpecialCharacterIfRequired)
                .WithMessage("Password must contain a special character.");
    }

    private static bool HaveUppercaseIfRequired(string password) =>
        !PasswordPolicyConstants.RequireUppercase || password.Any(char.IsUpper);

    private static bool HaveLowercaseIfRequired(string password) =>
        !PasswordPolicyConstants.RequireLowercase || password.Any(char.IsLower);

    private static bool HaveDigitIfRequired(string password) =>
        !PasswordPolicyConstants.RequireDigit || password.Any(char.IsDigit);

    private static bool HaveSpecialCharacterIfRequired(string password) =>
        !PasswordPolicyConstants.RequireSpecialCharacter || password.Any(c => !char.IsLetterOrDigit(c));
}