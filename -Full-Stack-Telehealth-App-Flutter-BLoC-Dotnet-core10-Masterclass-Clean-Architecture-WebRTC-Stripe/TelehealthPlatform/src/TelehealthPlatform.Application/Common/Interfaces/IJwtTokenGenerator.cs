using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    (string AccessToken, DateTime ExpiresAtUtc) GenerateAccessToken(User user);
} 