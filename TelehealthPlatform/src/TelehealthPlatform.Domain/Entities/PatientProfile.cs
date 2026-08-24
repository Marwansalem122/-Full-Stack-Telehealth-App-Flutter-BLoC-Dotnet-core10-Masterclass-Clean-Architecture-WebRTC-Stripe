using TelehealthPlatform.Domain.Common;
using TelehealthPlatform.Domain.ValueObjects;

namespace TelehealthPlatform.Domain.Entities;

public class PatientProfile : Entity
{
    public Guid UserId { get; init; }
    public HealthProfile HealthProfile { get; private set; } = HealthProfile.Empty();
    public string? ContactDetails { get; private set; }

    public User User { get; private set; } = default!;

    private PatientProfile() { }

    public static PatientProfile CreateEmpty(Guid userId)
    {
        return new PatientProfile { UserId = userId };
    }

    public void UpdateHealthProfile(HealthProfile healthProfile) => HealthProfile = healthProfile;

    public void UpdateContactDetails(string? contactDetails) => ContactDetails = contactDetails;
}