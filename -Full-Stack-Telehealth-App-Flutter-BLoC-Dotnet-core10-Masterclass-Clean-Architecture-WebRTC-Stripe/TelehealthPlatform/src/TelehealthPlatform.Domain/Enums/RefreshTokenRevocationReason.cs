namespace TelehealthPlatform.Domain.Enums;

public enum RefreshTokenRevocationReason
{
    ReplacedByRotation,
    PasswordChanged,
    UserLogout,
    ReuseDetected,
    AdminAction
}