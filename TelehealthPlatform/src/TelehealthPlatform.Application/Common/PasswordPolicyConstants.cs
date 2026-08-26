namespace TelehealthPlatform.Application.Common;

/// <summary>
/// Single source of truth for the password policy — referenced by
/// RegisterCommandValidator, ChangePasswordCommandValidator, and
/// GetPasswordPolicyQueryHandler. Change the rule here once; every
/// consumer picks it up automatically instead of drifting independently.
/// </summary>
public static class PasswordPolicyConstants
{
    public const int MinLength = 8;
    public const bool RequireUppercase = false;
    public const bool RequireLowercase = false;
    public const bool RequireDigit = false;
    public const bool RequireSpecialCharacter = false;
}