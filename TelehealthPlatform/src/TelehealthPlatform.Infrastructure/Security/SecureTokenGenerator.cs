using System.Security.Cryptography;
using TelehealthPlatform.Application.Common.Interfaces;

namespace TelehealthPlatform.Infrastructure.Security;

public class SecureTokenGenerator : ISecureTokenGenerator
{
    public string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('='); // URL-safe — this token often ends up in an email link's query string
}