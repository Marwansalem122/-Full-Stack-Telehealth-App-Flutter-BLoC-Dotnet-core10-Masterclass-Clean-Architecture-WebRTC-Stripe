using MediatR;

namespace TelehealthPlatform.Application.Auth.ChangePassword;

/// <summary>UserId comes from the authenticated JWT claims (set by the
/// controller from HttpContext.User), never from the request body —
/// otherwise a caller could pass someone else's UserId.</summary>
public record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : IRequest;