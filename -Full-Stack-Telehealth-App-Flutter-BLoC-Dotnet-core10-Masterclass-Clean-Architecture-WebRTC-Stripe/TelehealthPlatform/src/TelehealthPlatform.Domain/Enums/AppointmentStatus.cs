namespace TelehealthPlatform.Domain.Enums;

public enum AppointmentStatus
{
    PendingPayment,
    Confirmed,
    InProgress,
    Completed,
    PaymentFailed,
    Cancelled,
    Rescheduled,
    NoShow  
}