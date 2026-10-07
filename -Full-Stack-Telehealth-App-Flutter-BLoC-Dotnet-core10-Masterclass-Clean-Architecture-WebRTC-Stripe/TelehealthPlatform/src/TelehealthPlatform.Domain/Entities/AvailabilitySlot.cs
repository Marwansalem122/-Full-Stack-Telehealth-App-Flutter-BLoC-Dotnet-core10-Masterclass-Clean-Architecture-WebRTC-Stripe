using TelehealthPlatform.Domain.Common;

namespace TelehealthPlatform.Domain.Entities;

/// <summary>
/// Recurring weekly rule. DayOfWeek + Start/EndTimeUtc represent the
/// UTC-converted recurring rule as stored (ERD). The client-facing API
/// (PUT /consultants/me/availability) accepts LOCAL time + uses
/// ConsultantProfile.TimeZoneId to convert on write (API Contract v2).
/// NOTE: because this is a *recurring* rule, not a fixed instant, a naive
/// "convert once at write time" approach can drift by an hour across a
/// DST transition. For v1 this is accepted as a known limitation (documented
/// here rather than solved with a full IANA-aware recurring-rule library) —
/// revisit if it causes real scheduling errors.
/// </summary>
public class AvailabilitySlot : Entity
{
    public Guid ConsultantId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTimeUtc { get; private set; }
    public TimeOnly EndTimeUtc { get; private set; }

    public ConsultantProfile Consultant { get; private set; } = default!;

    private AvailabilitySlot() { }

    public static AvailabilitySlot Create(Guid consultantId, DayOfWeek dayOfWeek, TimeOnly startTimeUtc, TimeOnly endTimeUtc)
    {
        if (endTimeUtc <= startTimeUtc)
            throw new ArgumentException("End time must be after start time.");

        return new AvailabilitySlot
        {
            ConsultantId = consultantId,
            DayOfWeek = dayOfWeek,
            StartTimeUtc = startTimeUtc,
            EndTimeUtc = endTimeUtc
        };
    }
}