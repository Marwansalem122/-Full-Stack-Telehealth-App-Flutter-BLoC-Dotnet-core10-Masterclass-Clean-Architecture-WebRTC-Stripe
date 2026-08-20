# Telehealth Platform — Entity Relationship Diagram

**Version:** v1.1 — Core entities + Supporting entities + Security entities

```mermaid
erDiagram
    %% Core entities
    USERS ||--o| CONSULTANT_PROFILES : "has (if consultant)"
    USERS ||--o| PATIENT_PROFILES : "has (if patient)"
    CONSULTANT_PROFILES ||--o{ AVAILABILITY_SLOTS : defines
    CONSULTANT_PROFILES ||--o{ APPOINTMENTS : accepts
    PATIENT_PROFILES ||--o{ APPOINTMENTS : books
    APPOINTMENTS ||--o| CONSULTATIONS : produces

    %% Supporting entities
    APPOINTMENTS ||--o| PAYMENTS : "paid via"
    APPOINTMENTS ||--o{ CHAT_MESSAGES : contains
    USERS ||--o{ CHAT_MESSAGES : sends
    CONSULTATIONS ||--o| AI_SUMMARIES : generates
    CONSULTATIONS ||--o| AI_JOBS : "processed by"
    USERS ||--o{ NOTIFICATIONS : receives
    USERS ||--o{ REFRESH_TOKENS : has
    USERS ||--o{ PASSWORD_RESET_TOKENS : "requests"
    USERS ||--o{ FAILED_LOGIN_ATTEMPTS : "attempts"

    USERS {
        guid Id PK
        string Name
        string Email UK
        string PasswordHash
        string Role
        bool EmailConfirmed
        string EmailVerificationTokenHash
        datetime EmailVerificationSentAt
        datetime CreatedAt
    }

    CONSULTANT_PROFILES {
        guid Id PK
        guid UserId FK
        string Specialty
        string Bio
        string TimeZoneId
        bool IsVerified
        string ProfileImageUrl
    }

    PATIENT_PROFILES {
        guid Id PK
        guid UserId FK
        string HealthInfo
        string ContactDetails
    }

    AVAILABILITY_SLOTS {
        guid Id PK
        guid ConsultantId FK
        int DayOfWeek
        time StartTimeUtc
        time EndTimeUtc
    }

    APPOINTMENTS {
        guid Id PK
        guid PatientId FK
        guid ConsultantId FK
        datetime ScheduledStartUtc
        string Status
        string CancellationReason
        datetime CancelledAt
        string IdempotencyKey UK
    }

    CONSULTATIONS {
        guid Id PK
        guid AppointmentId FK
        datetime StartedAt
        datetime EndedAt
        int DurationMinutes
        string Status
    }

    PAYMENTS {
        guid Id PK
        guid AppointmentId FK
        decimal Amount
        string Currency
        string StripePaymentIntentId
        string IdempotencyKey
        string Status
        datetime CreatedAt
        datetime PaidAt
    }

    CHAT_MESSAGES {
        guid Id PK
        guid AppointmentId FK
        guid SenderId FK
        string Content
        string MessageType
        datetime SentAt
        datetime ReadAt
    }

    AI_SUMMARIES {
        guid Id PK
        guid ConsultationId FK
        string ChiefComplaint
        json StructuredOutput
        datetime CreatedAt
    }

    AI_JOBS {
        guid Id PK
        guid ConsultationId FK
        string Status
        datetime ProcessingStartedAt
        int RetryCount
        string Provider
        string PromptVersion
        string ErrorDetails
        datetime CreatedAt
    }

    NOTIFICATIONS {
        guid Id PK
        guid UserId FK
        string Type
        json Data
        bool IsRead
        datetime ReadAt
        datetime CreatedAt
    }

    REFRESH_TOKENS {
        guid Id PK
        guid UserId FK
        string TokenHash UK
        string FamilyId
        guid ReplacedByTokenId FK
        datetime ExpiresAt
        datetime CreatedAt
        datetime RevokedAt
        string RevocationReason
        string RequestIpAddress
        string UserAgent
    }

    STRIPE_WEBHOOK_EVENTS {
        guid Id PK
        string StripeEventId UK
        string EventType
        datetime ReceivedAt
        datetime ProcessedAt
    }

    PASSWORD_RESET_TOKENS {
        guid Id PK
        guid UserId FK
        string TokenHash UK
        datetime ExpiresAt
        datetime CreatedAt
        datetime UsedAt
        string RequestIpAddress
        string UserAgent
        bool IsUsed
    }

    FAILED_LOGIN_ATTEMPTS {
        guid Id PK
        string EmailAttempted
        string IpAddress
        string UserAgent
        datetime AttemptedAt
        bool WasSuccessful
    }
```

---

## Security Entities (added v1.1)

### `Users` — Modified
- `Email` → added `UK` (unique constraint was implied, now explicit)
- `EmailConfirmed` → new. `false` at registration; `true` after email verification. Gate for booking/payment.
- `EmailVerificationTokenHash` → new. SHA256 hash of the raw verification token sent via email. Cleared after verification.
- `EmailVerificationSentAt` → new. Tracks when the last verification email was sent (rate-limiting + expiry check).

### `RefreshTokens` — Modified
- `TokenHash` → added `UK` (unique constraint, was implied).
- `FamilyId` → new. Groups all tokens produced by rotation from the same initial login. Enables "nuclear option" on reuse detection.
- `ReplacedByTokenId` → new. Self-referencing FK. Audit trail: which token replaced this one during rotation.
- `RevocationReason` → new. Enum: `ReplacedByRotation`, `PasswordChanged`, `UserLogout`, `ReuseDetected`, `AdminAction`.
- `RequestIpAddress` / `UserAgent` → new. Minimal fingerprinting for v1 audit trail.

### `PasswordResetTokens` — New
Same security pattern as `RefreshTokens`: raw token never stored; only `SHA256(Token)` is persisted.
- `TokenHash` → `UK`. SHA256 of the 256-bit cryptographically random raw token.
- `ExpiresAt` → 1 hour absolute from `CreatedAt`.
- `IsUsed` → single-use enforcement. Once `true`, token is permanently invalid.
- `UsedAt` → audit trail.
- `RequestIpAddress` / `UserAgent` → audit trail.

### `FailedLoginAttempts` — New
Audit-only table (not used for lockout logic — that is handled by ASP.NET Core Identity).
- `EmailAttempted` → the email string submitted (may not exist in `Users`).
- `IpAddress` / `UserAgent` / `AttemptedAt` / `WasSuccessful` → forensic data for v2 alerting rules.
