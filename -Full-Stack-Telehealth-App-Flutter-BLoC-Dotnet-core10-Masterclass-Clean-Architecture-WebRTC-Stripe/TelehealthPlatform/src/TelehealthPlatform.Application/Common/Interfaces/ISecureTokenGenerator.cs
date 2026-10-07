namespace TelehealthPlatform.Application.Common.Interfaces;

/// <summary>Generates the RAW token (256-bit cryptographically random) for
/// refresh tokens, password resets, and email verification — the same
/// generator, three different purposes.</summary>
public interface ISecureTokenGenerator
{
    string Generate();
}