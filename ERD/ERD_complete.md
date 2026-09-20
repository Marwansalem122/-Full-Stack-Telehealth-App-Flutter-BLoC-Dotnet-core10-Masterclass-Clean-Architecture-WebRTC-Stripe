# Telehealth Platform — Entity Relationship Diagram

**Version:** v1.6 — Core entities + Supporting entities + Security entities + Refund Flow + HealthProfile Value Object + No-Show Detection + FCM Push Notifications + Payment Provider Abstraction (Country, Provider, PayoutLedgerEntries)

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
    PAYMENTS ||--o| PAYOUT_LEDGER_ENTRIES : "owes (Regional provider only)"
    CONSULTANT_PROFILES ||--o{ PAYOUT_LEDGER_ENTRIES : "is owed"
    APPOINTMENTS ||--o{ CHAT_MESSAGES : contains
    USERS ||--o{ CHAT_MESSAGES : sends
    CONSULTATIONS ||--o| AI_SUMMARIES : generates
    CONSULTATIONS ||--o| AI_JOBS : "processed by"
    USERS ||--o{ NOTIFICATIONS : receives
    USERS ||--o{ REFRESH_TOKENS : has
    USERS ||--o{ PASSWORD_RESET_TOKENS : "requests"
    USERS ||--o{ FAILED_LOGIN_ATTEMPTS : "attempts"
    USERS ||--o{ USER_DEVICE_TOKENS : "devices"

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
        string Country
        bool IsVerified
        string ProfileImageUrl
    }

    PATIENT_PROFILES {
        guid Id PK
        guid UserId FK
        int HeightCm
        decimal WeightKg
        string BloodType
        string SmokingStatus
        json Allergies
        json ChronicConditions
        json CurrentMedications
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
        datetime PatientJoinedAt
        datetime ConsultantJoinedAt
        int DurationMinutes
        string Status
    }

    PAYMENTS {
        guid Id PK
        guid AppointmentId FK
        decimal Amount
        string Currency
        string Provider
        string StripePaymentIntentId
        string StripeRefundId
        string IdempotencyKey
        string Status
        string RefundStatus
        datetime CreatedAt
        datetime PaidAt
        datetime RefundedAt
    }

    PAYOUT_LEDGER_ENTRIES {
        guid Id PK
        guid ConsultantId FK
        guid PaymentId FK
        decimal AmountOwed
        string Currency
        string Status
        datetime CreatedAt
        datetime SettledAt
        guid SettledByAdminUserId
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


---

### `PatientProfiles` — HealthProfile Value Object (added v1.3)

Replaced the v1.2 `HealthInfo` string field with a structured Value Object. This is an **owned entity** (not a separate table) — stored as JSON columns or via EF Core `OwnsOne` / `ToJson()`.

**Fields:**
- `HeightCm` → nullable int, validated range 50–300 cm
- `WeightKg` → nullable decimal, validated range 2–500 kg
- `BloodType` → enum: `Unknown`, `APositive`, `ANegative`, `BPositive`, `BNegative`, `ABPositive`, `ABNegative`, `OPositive`, `ONegative`
- `SmokingStatus` → enum: `Unknown`, `Never`, `Former`, `Current`
- `Allergies` → JSON array of strings (e.g., `["Penicillin", "Peanuts"]`)
- `ChronicConditions` → JSON array of strings (e.g., `["Hypertension", "Type 2 Diabetes"]`)
- `CurrentMedications` → JSON array of strings (e.g., `["Metformin 500mg", "Lisinopril 10mg"]`)

**Why not a string?**
- Enables validation (range checks, enum constraints)
- Enables querying ("find all patients with diabetes")
- Enables structured AI context for consultation summaries
- Enables clean Flutter form UI (separate fields, not a text blob)

**All fields are nullable** — patients are not required to provide health data, but when they do it must be structured and valid.


### `Consultations` — No-Show Detection Fields (added v1.4)

Added two nullable timestamp fields to support automatic no-show detection:

- `PatientJoinedAt` → set when the patient successfully calls `JoinCall` (SignalR hub method). Null if patient never joined.
- `ConsultantJoinedAt` → set when the consultant successfully calls `JoinCall`. Null if consultant never joined.
- `StartedAt` → set when the *first* participant joins (call transitions to `InProgress`). Null if call never started.

**No-show evaluation (BackgroundService, 15 minutes after `ScheduledStartUtc`):**
- `StartedAt IS NULL` + `PatientJoinedAt IS NULL` + `ConsultantJoinedAt HAS VALUE` → **Patient no-show**
- `StartedAt IS NULL` + `PatientJoinedAt HAS VALUE` + `ConsultantJoinedAt IS NULL` → **Consultant no-show**
- `StartedAt IS NULL` + both null → **Mutual no-show**

These fields enable the refund policy (§3.2.1) to be applied automatically without manual intervention.


### `UserDeviceTokens` — FCM Push Notification Tokens (added v1.5)

Stores Firebase Cloud Messaging (FCM) device tokens per user. Multiple devices per user are supported (e.g., phone + tablet).

- `Token` → the FCM device token obtained from `FirebaseMessaging.instance.getToken()`. `UK` constraint prevents duplicate tokens across users.
- `Platform` → `"android"` or `"ios"`. Used to route to the correct FCM API (Android vs iOS have different payload structures).
- `LastUsedAt` → updated when a push notification is successfully delivered to this token. Helps identify stale tokens for cleanup.
- **Token refresh:** FCM tokens can change (app reinstall, token rotation). Flutter detects the change via `onTokenRefresh` and re-registers via `POST /notifications/device-token`. The backend replaces the old token.
- **Cleanup:** A background job (v1 minimal — can be manual for now) removes tokens where `LastUsedAt < Now.AddMonths(-3)`.


### `ConsultantProfiles.Country` and `Payments.Provider` — added v1.6

Two small but load-bearing fields, added together because they're causally linked:

- `ConsultantProfiles.Country` → previously missing entirely from the ERD. Required at profile completion (alongside `TimeZoneId`). Determines which `IPaymentProvider` a consultant's appointments route through (README §3.3.1) — the platform launches in Egypt + the Arab world first, and **Stripe Connect does not support payouts to Egypt**, so a single global payment path was never actually workable.
- `Payments.Provider` → records which concrete provider processed a given payment (`Stripe` or `Regional`), set at the time the `Payment` is created based on the consultant's `Country`. This is what tells the system whether to expect a Stripe webhook or a regional-gateway webhook for a given payment, and whether a `PayoutLedgerEntries` row should exist for it.

### `PayoutLedgerEntries` — Manual Payout Tracking (added v1.6)

Exists only for payments where `Provider = Regional` — there is no automatic Stripe-style transfer to reconcile against, so the platform tracks what it owes each consultant explicitly.

- `ConsultantId` / `PaymentId` → which consultant is owed money, and for which specific payment.
- `AmountOwed` / `Currency` → the consultant's 80% share, in the original charge currency (README §3.3.1 — never converted to USD for this calculation).
- `Status` → `Pending`, `Paid`, or `Refunded`. `Refunded` is set if the underlying `Payment` is refunded before this entry was settled — the row is kept (not deleted) as an audit record. A refund arriving *after* an entry is already `Paid` is a clawback scenario (the consultant already received money for a now-refunded appointment) and requires manual admin reconciliation — the system does not attempt to resolve this automatically.
- `SettledAt` / `SettledByAdminUserId` → set when an admin confirms the bank transfer was actually made, via the minimal Admin Dashboard (README §3.3.2). This is a manual confirmation, not an automated payment event — there is no webhook for a bank transfer the platform itself initiates outside the system.

**Not created at all for `Provider = Stripe` payments** — those are paid out automatically via Stripe Connect and never touch this table.
