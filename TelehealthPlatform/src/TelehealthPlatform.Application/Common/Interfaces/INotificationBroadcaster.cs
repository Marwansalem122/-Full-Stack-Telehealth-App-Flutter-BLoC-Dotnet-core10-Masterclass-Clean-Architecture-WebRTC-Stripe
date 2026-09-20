using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Application.Common.Interfaces;

/// <summary>Abstraction over SignalR (Architecture Document, Section 12) —
/// Application sends "notify whoever's listening for this user" without
/// knowing Hub/Client specifics.</summary>
public interface INotificationBroadcaster
{
    Task BroadcastAsync(Notification notification, CancellationToken ct);
}