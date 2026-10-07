using System.Security.Cryptography;
using System.Text;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Infrastructure.Security;

/// <summary>
/// SHA256 — deliberately FAST, unlike BCryptPasswordHasher above. Refresh
/// tokens / reset tokens / verification tokens are already 256-bit
/// cryptographically random values (SecureTokenGenerator below) — a slow
/// hash adds no security benefit here (there's no dictionary attack surface
/// against a random 256-bit value) and would only waste CPU on every
/// single API request that includes a bearer/refresh token.
/// </summary>
public class Sha256TokenHasher : ITokenHasher
{
    public string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes); // uppercase hex — fine for a DB-indexed lookup value
    }
}