using TelehealthPlatform.Domain.Common;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// IsVerified defaults to false — a consultant can complete their profile
/// but won't appear in patient search until manually verified (Requirements
/// Document, Section 3.1). No admin UI for this in v1; it's a direct DB
/// update. TimeZoneId is required from day one — retrofitting it later is
/// painful (Requirements §3.2).
/// </summary>
public class ConsultantProfile : Entity
{
    public Guid UserId { get; private set; }
    public string Specialty { get; private set; } = default!;
    public string Bio { get; private set; } = default!;
    public string TimeZoneId { get; private set; } = default!;
    public bool IsVerified { get; private set; }
    public string? ProfileImageUrl { get; private set; }

    public User User { get; private set; } = default!;
    public ICollection<AvailabilitySlot> AvailabilitySlots { get; private set; } = new List<AvailabilitySlot>();

    private ConsultantProfile() { }

    public static ConsultantProfile CreateEmpty(Guid userId, string timeZoneId)
    {
        // Created transactionally alongside the User at registration
        // (API Contract, POST /auth/register) — starts minimal, filled
        // in later via PUT /consultants/me.
        return new ConsultantProfile
        {
            UserId = userId,
            Specialty = string.Empty,
            Bio = string.Empty,
            TimeZoneId = timeZoneId,
            IsVerified = false
        };
    }

    public void UpdateProfile(string specialty, string bio, string timeZoneId, string? profileImageUrl)
    {
        Specialty = specialty;
        Bio = bio;
        TimeZoneId = timeZoneId;
        ProfileImageUrl = profileImageUrl;
        // Note: IsVerified is intentionally NOT settable here — it's
        // read-only via any client-facing endpoint in v1 (API Contract).
    }
}