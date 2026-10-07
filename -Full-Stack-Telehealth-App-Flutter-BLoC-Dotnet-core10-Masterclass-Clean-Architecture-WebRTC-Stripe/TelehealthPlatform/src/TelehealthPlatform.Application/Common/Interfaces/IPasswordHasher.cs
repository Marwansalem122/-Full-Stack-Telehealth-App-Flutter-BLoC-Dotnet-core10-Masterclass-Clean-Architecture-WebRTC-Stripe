namespace TelehealthPlatform.Application.Common.Interfaces;

/// <summary>Bcrypt/Argon2-style hashing for USER PASSWORDS specifically —
/// deliberately a separate interface from ITokenHasher (refresh/reset
/// tokens use SHA256, a completely different algorithm with different
/// security properties; conflating the two is a common mistake).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}