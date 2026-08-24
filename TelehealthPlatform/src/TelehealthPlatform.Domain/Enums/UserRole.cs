using System;
using System.Collections.Generic;
using System.Text;

namespace TelehealthPlatform.Domain.Enums
{
    /// <summary>
    /// Set once at registration and treated as immutable (Requirements Document,
    /// Section 3.1). This is what enforces "a User is a Patient XOR a Consultant,
    /// never both" at the application layer — command handlers that create a
    /// ConsultantProfile/PatientProfile must check this before proceeding.
    /// </summary>
    public enum UserRole
    {
        Patient,
        Consultant
    }
}
