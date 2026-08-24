using System;
using System.Collections.Generic;
using System.Text;

namespace TelehealthPlatform.Domain.Common
{
    /// <summary>
    /// Deliberately minimal — no CreatedAt/ModifiedAt/soft-delete here yet.
    /// Individual entities declare their own audit fields where the ERD
    /// specifies them (e.g. Appointment.CancelledAt), rather than forcing
    /// every entity to carry fields it doesn't need. A generic audit base
    /// class is a reasonable v2 refactor once the AuditLog table (Requirements
    /// Document, Section 5) is actually being built — not before.
    /// </summary>
    public abstract class Entity
    {
        public Guid Id { get; init; }
    }
}
