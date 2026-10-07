using System;
using System.Collections.Generic;
using System.Text;

namespace TelehealthPlatform.Domain.Enums
{
    /// <summary>
    /// v1 event set (Requirements Document, Section 3.6).
    /// </summary>
    public enum NotificationType
    {
        AppointmentBooked,
        AppointmentConfirmed,
        AppointmentCancelled,
        AppointmentStartingSoon,
        ConsultantJoinedConsultation,
        ConsultationSummaryReady
    }
}
