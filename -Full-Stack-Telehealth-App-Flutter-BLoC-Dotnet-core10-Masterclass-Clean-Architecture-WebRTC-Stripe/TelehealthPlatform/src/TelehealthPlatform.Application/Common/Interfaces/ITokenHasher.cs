namespace TelehealthPlatform.Application.Common.Interfaces;

/// <summary>SHA256 for refresh tokens / password-reset tokens / email
/// verification tokens (Requirements §5, Security Deep-Dive §2.2/§6) —
/// these are already high-entropy random values, so a fast hash is
/// correct here; slow password-hashing algorithms would be the wrong
/// tool for this job.</summary>
public interface ITokenHasher
{
    string Hash(string rawToken);
}