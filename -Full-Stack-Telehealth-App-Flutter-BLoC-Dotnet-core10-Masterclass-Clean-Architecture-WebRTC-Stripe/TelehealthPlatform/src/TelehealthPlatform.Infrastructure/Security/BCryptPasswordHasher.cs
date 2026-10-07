using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Infrastructure.Security;

/// <summary>
/// BCrypt is deliberately SLOW (adaptive work factor) — correct tool for
/// PASSWORDS specifically, unlike ITokenHasher below which needs to be
/// fast (see SHA256TokenHasher's doc comment for why).
/// </summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12; // adjust upward as hardware improves; 12 is a reasonable 2026 default

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}