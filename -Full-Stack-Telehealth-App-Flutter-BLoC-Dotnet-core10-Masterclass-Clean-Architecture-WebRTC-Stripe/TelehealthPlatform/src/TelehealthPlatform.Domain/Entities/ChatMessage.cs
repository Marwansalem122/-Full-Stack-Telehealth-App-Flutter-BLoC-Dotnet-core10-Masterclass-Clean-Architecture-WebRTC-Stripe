using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Content is encrypted at rest via an EF Core ValueConverter configured
/// in ChatMessageConfiguration (Requirements Document, Section 3.4 / 5) —
/// this class works with plaintext; encryption is an Infrastructure-layer
/// concern, not a Domain-layer one. Authorization (sender must be a
/// participant) is enforced in the SignalR Hub / command handler, not here
/// — a plain FK can't express "must be one of these two specific users"
/// (Requirements §3.4).
/// </summary>
public class ChatMessage : Entity
{
    public Guid AppointmentId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = default!;
    public ChatMessageType MessageType { get; private set; }
    public DateTime SentAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    private ChatMessage() { }

    public static ChatMessage CreateText(Guid appointmentId, Guid senderId, string content)
    {
        return new ChatMessage
        {
            AppointmentId = appointmentId,
            SenderId = senderId,
            Content = content,
            MessageType = ChatMessageType.Text,
            SentAtUtc = DateTime.UtcNow
        };
    }

    public static ChatMessage CreateSystemMessage(Guid appointmentId, string content)
    {
        // No SenderId from a real user — caller must supply a system/service
        // identity if the schema requires SenderId to be non-null; adjust
        // if a nullable SenderId is preferred for system messages.
        return new ChatMessage
        {
            AppointmentId = appointmentId,
            Content = content,
            MessageType = ChatMessageType.System,
            SentAtUtc = DateTime.UtcNow
        };
    }

    public void MarkRead(DateTime readAtUtc) => ReadAtUtc = readAtUtc;
}