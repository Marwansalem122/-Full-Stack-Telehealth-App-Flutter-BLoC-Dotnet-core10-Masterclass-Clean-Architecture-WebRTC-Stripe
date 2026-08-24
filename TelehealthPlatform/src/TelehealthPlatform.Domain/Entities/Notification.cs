using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// The durable record — SignalR is only the delivery mechanism (Sequence
/// Diagrams, "persist first, notify second"). Always created *after* the
/// state change it describes is committed, never before.
/// </summary>
public class Notification : Entity
{
    public Guid UserId { get; private set; }
    public NotificationType Type { get; init; }
    public string DataJson { get; private set; } = default!;
    public bool IsRead { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private Notification() { }

    public static Notification Create(Guid userId, NotificationType type, string dataJson)
    {
        return new Notification
        {
            UserId = userId,
            Type = type,
            DataJson = dataJson,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void MarkRead(DateTime nowUtc)
    {
        IsRead = true;
        ReadAtUtc = nowUtc;
    }
}