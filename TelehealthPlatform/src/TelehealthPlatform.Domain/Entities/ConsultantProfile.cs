using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.Entities;

public class ConsultantProfile : Entity
{
    public Guid UserId { get; init; }
    public string Specialty { get; private set; } = default!;
    public string Bio { get; private set; } = default!;
    public string? TimeZoneId { get; private set; }  // بقت nullable
    public bool IsVerified { get; private set; }
    public string? ProfileImageUrl { get; private set; }

    public User User { get; private set; } = default!;
    public ICollection<AvailabilitySlot> AvailabilitySlots { get; private set; } = new List<AvailabilitySlot>();

    private ConsultantProfile() { }

    public static ConsultantProfile CreateEmpty(Guid userId)
    {
        // TimeZoneId يفضل null لحد ما الدكتور يحدده عبر PUT /consultants/me
        return new ConsultantProfile
        {
            UserId = userId,
            Specialty = string.Empty,
            Bio = string.Empty,
            IsVerified = false
        };
    }

    public void UpdateProfile(string specialty, string bio, string? timeZoneId, string? profileImageUrl)
    {
        Specialty = specialty;
        Bio = bio;
        if (timeZoneId is not null) TimeZoneId = timeZoneId;
        ProfileImageUrl = profileImageUrl;
    }

    /// <summary>
    /// Enforces the API Contract precondition explicitly at the Domain
    /// layer too — TimeZoneId must be set before availability can exist.
    /// The API returns 409 timezone-required based on this same check.
    /// </summary>
    public void EnsureTimeZoneIsSet()
    {
        if (TimeZoneId is null)
            throw new InvalidOperationException("TimeZoneId must be set before managing availability.");
    }
}