using System;
using System.Collections.Generic;
using System.Text;

namespace TelehealthPlatform.Domain.Enums;

/// <summary>
/// Patient/Consultant are set at self-registration and immutable
/// (Requirements §3.1 — XOR rule). Admin is NEVER self-registered — it's
/// provisioned directly in the database (README §3.3.2) and is explicitly
/// exempt from the XOR profile-creation rule: an Admin has neither a
/// ConsultantProfile nor a PatientProfile, by design.
/// </summary>
public enum UserRole
{
    Patient,
    Consultant,
    Admin
}