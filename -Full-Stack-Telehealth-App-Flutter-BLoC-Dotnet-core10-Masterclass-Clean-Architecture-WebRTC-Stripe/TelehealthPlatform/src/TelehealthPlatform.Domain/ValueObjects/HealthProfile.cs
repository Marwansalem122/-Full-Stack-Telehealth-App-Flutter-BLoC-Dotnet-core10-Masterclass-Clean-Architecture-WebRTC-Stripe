using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Domain.ValueObjects;

/// <summary>
/// Owned entity (EF Core OwnsOne / ToJson) — not a separate table with its
/// own Id. Replaces the old PatientProfile.HealthInfo string field
/// (ERD v1.3). All fields nullable — patients aren't required to provide
/// health data, but when they do it must be structured and validated.
/// </summary>
public class HealthProfile
{
    public int? HeightCm { get; private set; }
    public decimal? WeightKg { get; private set; }
    public BloodType BloodType { get; private set; } = BloodType.Unknown;
    public SmokingStatus SmokingStatus { get; private set; } = SmokingStatus.Unknown;
    public List<string> Allergies { get; private set; } = new();
    public List<string> ChronicConditions { get; private set; } = new();
    public List<string> CurrentMedications { get; private set; } = new();

    private HealthProfile() { }

    public static HealthProfile Create(
        int? heightCm, decimal? weightKg, BloodType bloodType, SmokingStatus smokingStatus,
        IEnumerable<string>? allergies, IEnumerable<string>? chronicConditions, IEnumerable<string>? currentMedications)
    {
        if (heightCm is < 50 or > 300)
            throw new ArgumentOutOfRangeException(nameof(heightCm), "Height must be between 50 and 300 cm.");

        if (weightKg is < 2 or > 500)
            throw new ArgumentOutOfRangeException(nameof(weightKg), "Weight must be between 2 and 500 kg.");

        return new HealthProfile
        {
            HeightCm = heightCm,
            WeightKg = weightKg,
            BloodType = bloodType,
            SmokingStatus = smokingStatus,
            Allergies = allergies?.ToList() ?? new List<string>(),
            ChronicConditions = chronicConditions?.ToList() ?? new List<string>(),
            CurrentMedications = currentMedications?.ToList() ?? new List<string>()
        };
    }

    public static HealthProfile Empty() => new();
}