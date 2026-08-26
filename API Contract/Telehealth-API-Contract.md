# AI Telehealth Platform — API Contract

**System Design Series · Document 4 of 4** (ERD → Architecture → Sequence Diagrams → **API Contract**)
**Stack:** ASP.NET Core 10 (Clean Architecture, CQRS/MediatR) + Flutter (Clean Architecture, BLoC)
**Status:** v6 — replaced PatientProfiles.HealthInfo string with structured HealthProfile value object (height, weight, blood type, allergies, chronic conditions, medications) (cancellation >24h triggers Stripe refund, confirmed by webhook; earnings exclude refunded payments) (Forgot/Reset Password, Email Verification, Change Password, Password Policy), aligned with Security Deep-Dive Document 5.

---

## How to read this document

This is the last design document before code. Every endpoint here is a direct implementation of a decision already locked in the first three documents — this file doesn't introduce new architecture, it just makes the wire format concrete: exact routes, request/response shapes, status codes, and who's allowed to call what.

Endpoints are grouped by feature area, matching the modules in the Architecture Document (`Auth`, `Appointments`, `Payments`, `Chat`, `Consultations`, `AI`, `Notifications`). The two endpoints with real branching complexity — `POST /appointments` and the Stripe webhook — get full detail sections because they implement the Sequence Diagrams exactly; the rest are documented more compactly since they're straightforward CRUD.

---

## 1. Conventions

### Base URL & versioning
```
https://api.telehealth.example.com/api/v1
```
All routes below are relative to this base.

### Authentication
Every endpoint except `POST /auth/register`, `POST /auth/login`, `POST /auth/refresh`, `POST /auth/forgot-password`, `POST /auth/reset-password`, `POST /auth/verify-email`, `POST /auth/resend-verification`, `GET /auth/password-policy`, and `POST /webhooks/stripe` requires:
```
Authorization: Bearer <access_token>
```
The access token is a JWT (see Requirements Document, Section 3.1 / Architecture Document, Section 16 — Auth Token Flow).

### Standard error response format
```json
{
  "type": "https://telehealth.example.com/errors/validation-failed",
  "title": "Validation Failed",
  "status": 400,
  "detail": "One or more fields are invalid.",
  "errors": {
    "scheduledStartUtc": ["Must be a future date/time."]
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00"
}
```
Follows the [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) shape (`application/problem+json`), which ASP.NET Core 10 supports natively via `ProblemDetails`. `errors` is present only for `400 Validation Failed` responses; other error types omit it.

### Standard status codes used throughout
| Code | Meaning | Used for |
|---|---|---|
| `200 OK` | Success, returning data | GET, and actions that don't create a resource |
| `201 Created` | Resource created | POST that creates something (returns `Location` header) |
| `202 Accepted` | Accepted, processing async | Not used in v1 — `AIJob` creation is internal, not client-facing async |
| `204 No Content` | Success, nothing to return | DELETE, actions that intentionally return no body |
| `400 Bad Request` | Validation failure | Malformed/invalid request body |
| `401 Unauthorized` | Missing/invalid/expired token | No valid JWT |
| `403 Forbidden` | Authenticated but not allowed | Resource-ownership failure (e.g. accessing another patient's appointment) |
| `404 Not Found` | Resource doesn't exist (or caller can't know it exists) | Invalid ID, or hiding existence from unauthorized callers |
| `409 Conflict` | State conflict | Slot no longer available, appointment already cancelled, etc. |
| `429 Too Many Requests` | Rate limit exceeded | AI endpoints (§3.5), Auth endpoints (§Security Deep-Dive), and global rate limiting |
| `502 Bad Gateway` | Upstream dependency failed | Stripe unreachable during PaymentIntent creation |

### Pagination
List endpoints use cursor-free offset pagination for v1 (data volumes are small enough that this is sufficient — no need for keyset pagination yet):
```
GET /appointments?page=1&pageSize=20
```
```json
{
  "items": [ /* ... */ ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 47
}
```

### Idempotency headers
Two distinct headers are used for two distinct purposes — don't conflate them (see Sequence Diagrams, Document 3):
- `Idempotency-Key` — client-generated GUID, required on `POST /appointments`. Protects against duplicate appointment creation from a lost response.
- Stripe's own idempotency key is set **server-side** by the API when calling Stripe — Flutter never talks to Stripe's PaymentIntent creation endpoint directly.

---

## 2. Endpoint Overview

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/auth/register` | — | Register as Patient or Consultant |
| POST | `/auth/login` | — | Log in, receive access + refresh token |
| POST | `/auth/refresh` | — | Rotate refresh token, issue new access token |
| POST | `/auth/logout` | ✓ | Revoke refresh token |
| POST | `/auth/forgot-password` | — | Request password reset link (anti-enumeration) |
| POST | `/auth/reset-password` | — | Reset password using token |
| POST | `/auth/verify-email` | — | Verify email address |
| POST | `/auth/resend-verification` | — | Resend verification email (anti-enumeration) |
| POST | `/auth/change-password` | ✓ | Change password (invalidates all sessions) |
| GET | `/auth/password-policy` | — | Get password requirements (public) |
| GET | `/patients/me` | ✓ (Patient) | Get own patient profile |
| PUT | `/patients/me` | ✓ (Patient) | Update own patient profile |
| GET | `/consultants/me` | ✓ (Consultant) | Get own consultant profile |
| PUT | `/consultants/me` | ✓ (Consultant) | Update own consultant profile |
| GET | `/consultants` | ✓ | Search verified consultants by specialty |
| GET | `/consultants/{id}/availability` | ✓ | Get bookable slots (converted to caller's local time) |
| PUT | `/consultants/me/availability` | ✓ (Consultant) | Set weekly availability |
| GET | `/consultants/me/earnings` | ✓ (Consultant) | Get earnings/payout summary |
| POST | `/appointments` | ✓ (Patient) | Book an appointment *(full detail below)* |
| GET | `/appointments/{id}` | ✓ | Get appointment details |
| GET | `/appointments` | ✓ | List own appointments |
| POST | `/appointments/{id}/cancel` | ✓ | Cancel an appointment |
| POST | `/webhooks/stripe` | Stripe signature | Payment confirmation *(full detail below)* |
| POST | `/consultations/{appointmentId}/complete` | ✓ (Consultant) | Mark consultation complete, trigger AI summary |
| GET | `/consultations/{appointmentId}` | ✓ | Get consultation details + AI summary (if ready) |
| GET | `/appointments/{id}/messages` | ✓ | Get chat history for an appointment |
| POST | `/ai/symptom-check` | ✓ (Patient) | Get AI-suggested specialty from symptoms |
| GET | `/notifications` | ✓ | List own notifications |
| POST | `/notifications/{id}/read` | ✓ | Mark a notification read |

**SignalR Hub** (`/hubs/telehealth`) — see Section 5.

---

## 3. Detailed Contracts — Complex Endpoints

### 3.1 `POST /appointments`

Implements the full flow from Sequence Diagrams, Document 3, Diagram 1.

**Headers:**
```
Authorization: Bearer <token>
Idempotency-Key: <client-generated GUID>
Content-Type: application/json
```

**Request:**
```json
{
  "consultantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "scheduledStartUtc": "2026-08-20T09:00:00Z"
}
```

**Validation:**
| Field | Rule |
|---|---|
| `consultantId` | Required, must reference a verified (`IsVerified = true`) consultant |
| `scheduledStartUtc` | Required, must be in the future, must align to the 30-minute grid |

**Response — `201 Created`:**
```json
{
  "appointmentId": "b3f1...",
  "status": "PendingPayment",
  "scheduledStartUtc": "2026-08-20T09:00:00Z",
  "consultantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "payment": {
    "clientSecret": "pi_3P.....secret_...."
  }
}
```

**Response — `409 Conflict`** (slot taken, or patient overlap — the filtered unique index violation surfaces here):
```json
{
  "type": ".../errors/slot-unavailable",
  "title": "Slot No Longer Available",
  "status": 409,
  "detail": "This time slot was booked by another patient, or you already have an appointment at this time."
}
```

**Response — `502 Bad Gateway`** (PaymentIntent creation failed — Appointment stays `PendingPayment`, retry with the same `Idempotency-Key`):
```json
{
  "type": ".../errors/payment-initialization-failed",
  "title": "Payment Initialization Failed",
  "status": 502,
  "detail": "Could not initialize payment. Please retry with the same request."
}
```

**Idempotent retry behavior (see Sequence Diagrams for the full branching logic):**
- Same `Idempotency-Key`, `PaymentIntent` already exists → `201` with the existing `appointmentId`. Since `StripeClientSecret` is never persisted (Requirements §3.3), the handler calls `GET /v1/payment_intents/{id}` against Stripe to re-retrieve the `clientSecret` before responding. The `status` field reflects the appointment's *current* state — on a retry where the webhook already confirmed payment, this will be `Confirmed` rather than `PendingPayment`.
- Same `Idempotency-Key`, no `PaymentIntent` yet → resumes and attempts `PaymentIntent` creation again, not a cached response.

---

### 3.2 `POST /webhooks/stripe`

Implements Sequence Diagrams, Document 3, Diagram 1 (webhook portion) **and** refund confirmation. **Not a normal client endpoint** — called by Stripe, authenticated via signature, not JWT.

**Headers:**
```
Stripe-Signature: t=...,v1=...
Content-Type: application/json
```

**Request:** raw Stripe event payload (unmodified, needed for signature verification).

**Behavior:**
1. Verify `Stripe-Signature` against the raw body. Invalid signature → `400`, event discarded, nothing written.
2. Attempt `INSERT` into `StripeWebhookEvents` with the event's `StripeEventId`. Constraint violation → event already processed → `200` immediately, no further action.
3. New event → process based on event type:
   - **`payment_intent.succeeded`**: single transaction: `Payment.Status = Paid`, `Appointment.Status = Confirmed`, create `Notification`. Commit. SignalR broadcast to patient.
   - **`charge.refunded`**: single transaction: `Payment.RefundStatus = Succeeded`, `Payment.RefundedAt = Now`, create `Notification` (to patient: "Your refund has been processed"). Commit. SignalR broadcast to patient.

**Response — `200 OK`** in all "handled" cases (including duplicates) — Stripe interprets anything other than `2xx` as "redeliver later," which is only desired for genuine processing failures, not duplicates.
```json
{ "received": true }
```

**Response — `400 Bad Request`** — signature verification failed.

---

### 3.3 `POST /consultations/{appointmentId}/complete`

Implements Sequence Diagrams, Document 3, Diagram 3.

**Authorization:** caller must be the Consultant on this appointment (resource ownership), and `Appointment.Status` must be `InProgress`.

**Request:** empty body.

**Response — `200 OK`** (returned immediately — the AI summary is not ready yet, generated asynchronously):
```json
{
  "consultationId": "c4a2...",
  "status": "Completed",
  "aiSummaryStatus": "Pending"
}
```

**Response — `403 Forbidden`** — caller is not the consultant for this appointment.
**Response — `409 Conflict`** — appointment isn't `InProgress` (e.g. already completed, or never started).

---

### 3.4 `GET /consultations/{appointmentId}`

Poll or fetch-on-notification to retrieve the AI summary once ready.

**Response — `200 OK`:**
```json
{
  "consultationId": "c4a2...",
  "status": "Completed",
  "startedAt": "2026-08-20T09:00:00Z",
  "endedAt": "2026-08-20T09:28:00Z",
  "aiSummary": {
    "status": "Completed",
    "chiefComplaint": "Persistent headache, 3 days",
    "keyPoints": ["...", "..."],
    "recommendations": ["..."],
    "redFlags": [],
    "disclaimer": "This summary is AI-generated and not a substitute for professional medical judgment."
  }
}
```
If `AIJob` hasn't completed yet, `aiSummary.status` is `"Pending"` or `"Processing"` and the other fields are omitted. If it failed permanently (`MaxRetries` reached), `aiSummary.status` is `"Failed"`.

---

## 4. Compact Contracts — Remaining Endpoints

### Auth

**`POST /auth/register`**
```json
// Request
{ "email": "...", "password": "...", "role": "Patient" }
// Response 201
{ "userId": "...", "role": "Patient", "emailConfirmed": false }
```
`role` is immutable after registration (Requirements §3.1 — mutual exclusivity enforcement). Registration creates the `User` **and** the corresponding empty `PatientProfile`/`ConsultantProfile` row in the same transaction — not as a separate follow-up call. This keeps the invariant "every `User` has exactly one matching profile" true from the moment registration succeeds, rather than leaving a window where a `User` exists with no profile at all. The profile starts empty/minimal (e.g. `ConsultantProfile.IsVerified = false`, no specialty/bio yet) and is filled in via `PUT /patients/me` / `PUT /consultants/me` afterward.

**Email verification:** `EmailConfirmed` starts `false`. A verification email is sent automatically on registration. The user cannot book appointments or make payments until `EmailConfirmed = true`. `POST /auth/forgot-password` will not send a reset email to unverified addresses (but still returns the same 200 response to prevent enumeration).

**`POST /auth/login`**
```json
// Request
{ "email": "...", "password": "..." }
// Response 200
{ "accessToken": "eyJ...", "refreshToken": "8f3e...", "expiresIn": 3600 }
```
**Account lockout:** After 5 consecutive failed attempts, the account is locked for 15 minutes. The error response remains generic (`401 Unauthorized`) regardless of whether the password is wrong, the account is locked, or the email doesn't exist — this prevents user enumeration and credential stuffing.

**`POST /auth/refresh`**
```json
// Request
{ "refreshToken": "8f3e..." }
// Response 200
{ "accessToken": "eyJ...", "refreshToken": "9a1c...", "expiresIn": 3600 }
```
Server hashes the incoming token and compares against `RefreshTokens.TokenHash` (never stores/compares raw — Requirements §5 security note). **Rotation:** every refresh issues a new token and revokes the old one. **Reuse detection:** if a revoked token is used again, the entire token family is invalidated (all refresh tokens for that login session are deleted) — this indicates theft. The response is always generic `401 Unauthorized` regardless of the failure reason (expired, revoked, or reuse detected) to prevent information leakage.

**`POST /auth/logout`**
```json
// Request
{ "refreshToken": "8f3e..." }
// Response 204 No Content
```
The `Authorization: Bearer <accessToken>` header alone doesn't identify *which* refresh token to revoke — a user could have more than one active session/device in the future (Architecture Document, Section 21 notes richer `RefreshToken` fields like `DeviceId` as a deliberate v2 addition; v1 doesn't track multiple sessions explicitly, but the revocation mechanism still needs the specific token). The server hashes the incoming `refreshToken` and looks up `RefreshTokens.TokenHash` (same comparison as `/auth/refresh`), then sets `RevokedAt`. `204` regardless of whether the token was found/already revoked — logout is idempotent from the client's perspective.

#### `POST /auth/forgot-password`
```json
// Request
{ "email": "user@example.com" }

// Response 200 (identical regardless of email existence — anti-enumeration)
{
  "message": "If this email is registered, you will receive a password reset link shortly."
}
```
**Rate limit:** 3 requests/hour per IP. **Timing-safe:** response delay is normalized to prevent timing attacks (same approximate duration whether email exists or not).

#### `POST /auth/reset-password`
```json
// Request
{
  "email": "user@example.com",
  "token": "raw-token-from-email",
  "newPassword": "NewSecurePass456!"
}

// Response 200
{ "message": "Password reset successful. Please log in again." }

// Response 400
{
  "type": ".../errors/invalid-token",
  "title": "Invalid or Expired Token",
  "status": 400,
  "detail": "The reset token is invalid, expired, or already used."
}
```
**Post-reset action:** All `RefreshTokens` for this user are deleted (forced re-login on all devices). Token is 256-bit random, SHA256-hashed in DB, single-use, 1-hour expiry.

#### `POST /auth/verify-email`
```json
// Request
{ "email": "user@example.com", "token": "raw-token-from-email" }

// Response 200
{ "emailConfirmed": true }

// Response 400
{
  "type": ".../errors/invalid-verification-token",
  "title": "Invalid or Expired Verification Token",
  "status": 400
}
```

#### `POST /auth/resend-verification`
```json
// Request
{ "email": "user@example.com" }

// Response 200 (identical regardless of email existence — anti-enumeration)
{ "message": "If this email is registered and unverified, a new link has been sent." }
```
**Rate limit:** 3 requests/hour per IP.

#### `POST /auth/change-password`
```json
// Request
{
  "currentPassword": "OldPass123!",
  "newPassword": "NewSecurePass456!"
}

// Response 200
{ "message": "Password changed successfully. Please log in again." }

// Response 400 (current password wrong)
{
  "type": ".../errors/validation-failed",
  "title": "Validation Failed",
  "status": 400,
  "errors": { "currentPassword": ["Current password is incorrect."] }
}

// Response 400 (new password fails policy)
{
  "type": ".../errors/validation-failed",
  "title": "Validation Failed",
  "status": 400,
  "errors": { "newPassword": ["Password must be at least 12 characters..."] }
}
```
**Post-change action:** All `RefreshTokens` for this user are deleted (forced re-login on all devices). Transactional: `UPDATE password` + `DELETE refresh tokens` in single DB transaction.

#### `GET /auth/password-policy`
```json
// Response 200 (public — no auth needed)
{
  "minLength": 12,
  "maxLength": 128,
  "requiredCharacterTypes": 3,
  "allowedSpecialCharacters": "!@#$%^&*()_+-=[]{}|;:,.<>?"
}
```
Flutter calls this on registration/change-password screens to display requirements before user starts typing.

---

### Profiles

**`GET /patients/me`**
```json
// Response 200
{
  "userId": "...",
  "healthProfile": {
    "heightCm": 175,
    "weightKg": 70.5,
    "bloodType": "APositive",
    "smokingStatus": "Never",
    "allergies": ["Penicillin", "Peanuts"],
    "chronicConditions": ["Hypertension"],
    "currentMedications": ["Lisinopril 10mg"],
    "bmi": 23.0
  },
  "contactDetails": "+20 123 456 7890"
}
```
`healthProfile` may be `null` if the patient has not provided health data yet. `bmi` is computed server-side from `heightCm` and `weightKg` when both are present.

**`PUT /patients/me`**
```json
// Request
{
  "healthProfile": {
    "heightCm": 175,
    "weightKg": 70.5,
    "bloodType": "APositive",
    "smokingStatus": "Never",
    "allergies": ["Penicillin", "Peanuts"],
    "chronicConditions": ["Hypertension"],
    "currentMedications": ["Lisinopril 10mg"]
  },
  "contactDetails": "+20 123 456 7890"
}

// Response 200
{
  "userId": "...",
  "healthProfile": { /* ... */ },
  "contactDetails": "+20 123 456 7890"
}
```
**Validation rules:**

| Field | Rule |
|---|---|
| `healthProfile.heightCm` | Optional. If provided, must be 50–300. |
| `healthProfile.weightKg` | Optional. If provided, must be 2.0–500.0. |
| `healthProfile.bloodType` | Optional. Enum: `Unknown`, `APositive`, `ANegative`, `BPositive`, `BNegative`, `ABPositive`, `ABNegative`, `OPositive`, `ONegative`. |
| `healthProfile.smokingStatus` | Optional. Enum: `Unknown`, `Never`, `Former`, `Current`. |
| `healthProfile.allergies` | Optional. Array of strings. Max 50 items. Each item max 100 chars. |
| `healthProfile.chronicConditions` | Optional. Array of strings. Max 50 items. Each item max 100 chars. |
| `healthProfile.currentMedications` | Optional. Array of strings. Max 50 items. Each item max 200 chars. |
| `contactDetails` | Optional. Max 500 chars. |

**Behavior:** `PUT` replaces the entire `healthProfile` object (same full-replace semantics as other v1 endpoints). Sending `healthProfile: null` clears all health data. Sending `healthProfile: {}` with all nulls keeps the object but clears all fields.

**`GET /consultants/me`** / **`PUT /consultants/me`** — same pattern. `IsVerified` is **read-only** via this endpoint — it's only ever flipped by direct DB update in v1 (Requirements §3.1), never through a client-facing field.

> **⚠️ `timeZoneId` is required for consultants.** The `PUT /consultants/me` request must include a valid IANA timezone ID (e.g., `"Africa/Cairo"`, `"Asia/Riyadh"`). While the field is technically optional in the request body (to allow partial updates of other fields), the consultant **cannot** set availability until `timeZoneId` is populated. See `PUT /consultants/me/availability` below.

**`GET /consultants?specialty=Cardiology&page=1`** — public-to-authenticated search, returns only `IsVerified = true` consultants.
```json
{
  "items": [
    { "consultantId": "...", "name": "...", "specialty": "Cardiology", "bio": "..." }
  ],
  "page": 1, "pageSize": 20, "totalCount": 8
}
```

**`GET /consultants/{id}/availability?fromUtc=...&toUtc=...`**
```json
{
  "slots": [
    { "scheduledStartUtc": "2026-08-20T09:00:00Z" },
    { "scheduledStartUtc": "2026-08-20T09:30:00Z" }
  ]
}
```
Computed server-side as `Weekly Availability − Existing Active Appointments` (Requirements §3.2), returned in UTC — Flutter converts to local display time.

**`PUT /consultants/me/availability`**
```json
{
  "slots": [
    { "dayOfWeek": 1, "startTime": "09:00", "endTime": "13:00" }
  ]
}
```
> **⚠️ Deliberately *not* `startTimeUtc`/`endTimeUtc` — this must be the consultant's local wall-clock time.** A recurring weekly rule like "Monday 09:00–13:00" only makes sense expressed in the consultant's own timezone; storing a fixed UTC offset for a *recurring* rule breaks the moment DST shifts, since the UTC equivalent of "9am Cairo time" isn't constant year-round. The server converts using `ConsultantProfile.TimeZoneId` at the point of computing actual bookable slots — the conversion happens on read (`GET /consultants/{id}/availability`, which *does* return UTC, per Requirements §3.2's rule that the backend always returns UTC to clients), not on write. `AvailabilitySlots.StartTimeUtc`/`EndTimeUtc` in the ERD remain UTC columns internally — but that's the *stored, computed* representation for a given date, not what this endpoint accepts as input.

**Precondition:** `ConsultantProfile.TimeZoneId` must be set. If null:

**Response — `409 Conflict`:**
```json
{
  "type": ".../errors/timezone-required",
  "title": "Timezone Required",
  "status": 409,
  "detail": "You must set your timezone in your profile before setting availability."
}
```

Replaces the full weekly schedule (simplest correct semantics for v1 — no partial-update merge logic to get wrong).

---

**`GET /consultants/me/earnings`**
```json
// Response 200
{
  "totalEarnings": 1500.00,
  "currency": "USD",
  "completedPaidConsultations": 10,
  "refundedConsultations": 2,
  "pendingPayout": 500.00
}
```
Aggregates from `Payments` where `Status = Paid` AND (`RefundStatus IS NULL` OR `RefundStatus != Succeeded`) and the associated appointment's consultant is the caller. **Refunded appointments are excluded from earnings.** `refundedConsultations` counts appointments that were paid then refunded. `pendingPayout` counts completed consultations whose payment is still in Stripe's hold period (exact payout timing is a Stripe/platform detail, not a v1 design concern).

---

### Appointments

**`GET /appointments/{id}`** — `403` if caller is neither the patient nor the consultant on this appointment (resource ownership, Requirements §3.1).

> **⚠️ No-Show Policy Note:** Once an appointment is marked `NoShow` (automatically by the background job 15 minutes after scheduled time), it is **final**. There is no appeal, no manual override, and no reactivation. Both `JoinCall` and `SendMessage` will reject with `409 Conflict`. See Requirements §3.2.2 for the complete fault attribution and refund rules.

**`GET /appointments?status=Confirmed&page=1`** — lists the caller's own appointments (as patient or consultant, whichever role they have).

**`POST /appointments/{id}/cancel`**
> **See Requirements §3.2.1 for the complete Refund Policy table.** The following is the API implementation of that policy.

```json
// Request
{ "reason": "Schedule conflict" }
// Response 200 — cancelled WITH refund (more than 24h before)
{
  "appointmentId": "...",
  "status": "Cancelled",
  "refund": {
    "status": "Pending",
    "message": "Refund initiated. You will receive confirmation shortly."
  }
}

// Response 200 — cancelled WITHOUT refund (within 24h window)
{
  "appointmentId": "...",
  "status": "Cancelled",
  "refund": null,
  "message": "Appointment cancelled. Cancellation within 24 hours is not eligible for refund."
}
```
`403` if not a participant. `409` if not in `Confirmed` state (can't cancel something already `Completed`/`Cancelled`/`PendingPayment`).

**Refund Logic (implementation of Requirements §3.2.1):**
- If `Now() < ScheduledStartUtc - 24h` (more than 24h before):
  1. `Appointment.Status = Cancelled`
  2. `Payment.RefundStatus = Pending`
  3. Call Stripe API to create refund (`POST /v1/refunds` with `payment_intent` ID)
  4. Store `StripeRefundId` in `Payment`
  5. Return `200` with `refund.status = "Pending"`
  6. Stripe webhook `charge.refunded` later updates `RefundStatus = Succeeded` + `RefundedAt`
- If `Now() >= ScheduledStartUtc - 24h` (within 24h):
  1. `Appointment.Status = Cancelled` only
  2. No refund initiated
  3. Return `200` with `refund = null`

**Response — `409 Conflict`** (cancellation window expired — Requirements §8's fixed rule: free cancellation up to 24h before the appointment):
```json
{
  "type": ".../errors/cancellation-window-expired",
  "title": "Cancellation Window Expired",
  "status": 409,
  "detail": "Appointments can only be cancelled more than 24 hours before the scheduled time."
}
```
Server-side check: `409` when `Now() > ScheduledStartUtc - 24h`. Treated as a `409` (state/business-rule conflict) rather than `400` (malformed request) — the request itself is well-formed; it's the *current moment relative to the appointment* that makes it invalid, the same category as "slot no longer available.""

---

### Chat

**`GET /appointments/{id}/messages?page=1`** — REST endpoint for chat **history**; real-time delivery is SignalR only (Section 5). `403` if caller isn't a participant (Requirements §3.4 — chat authorization rule).
```json
{
  "items": [
    { "senderId": "...", "content": "...", "messageType": "Text", "sentAt": "...", "readAt": null }
  ],
  "page": 1, "pageSize": 50, "totalCount": 12
}
```

---

### AI

**`POST /ai/symptom-check`**
```json
// Request
{ "symptoms": "Persistent headache for 3 days, worse in the morning" }
// Response 200
{
  "suggestedSpecialty": "Neurology",
  "keyPoints": ["..."],
  "disclaimer": "This is not a medical diagnosis. Consult a specialist."
}
```
Rate-limited (Requirements §3.5 — 10 requests/minute/user). `429 Too Many Requests` on limit exceeded. Synchronous — unlike consultation summaries, symptom-checking has no persisted job/context, so there's nothing to make async.

---

### Notifications

**`GET /notifications?unreadOnly=true&page=1`**
```json
{
  "items": [
    { "id": "...", "type": "AppointmentConfirmed", "data": { "appointmentId": "..." }, "isRead": false, "createdAt": "..." }
  ],
  "page": 1, "pageSize": 20, "totalCount": 3
}
```

**`POST /notifications/{id}/read`** → `204 No Content`.

---

## 5. SignalR Hub Contract (`/hubs/telehealth`)

Implements Sequence Diagrams, Document 3, Diagrams 2 (WebRTC signaling) and the chat delivery side of Diagram 1's "persist first, notify second" pattern. Connection requires the same JWT (`Authorization: Bearer` on the connection handshake).

### Client → Server methods

| Method | Payload | Behavior |
|---|---|---|
| `SendMessage` | `{ appointmentId, content }` | Validates sender is a participant (Requirements §3.4), persists to `ChatMessages`, then broadcasts |
| `JoinCall` | `{ appointmentId }` | Validates participant + `Appointment.Status ∈ {Confirmed, InProgress}` (Sequence Diagrams, Diagram 2). **Rejects with error if `Appointment.Status = NoShow`** ("This appointment has been marked as no-show and cannot be joined"). On the *first* successful `JoinCall` for a `Confirmed` appointment, transitions `Appointment.Status` to `InProgress`, records `Consultation.PatientJoinedAt` or `Consultation.ConsultantJoinedAt` (depending on caller role), then relays signaling |
| `SendSignal` | `{ appointmentId, type: "offer"\|"answer"\|"ice-candidate", payload }` | Validates participant + `Appointment.Status ∈ {Confirmed, InProgress}`, then relays SDP/ICE data to the other participant — Hub never inspects or stores payload contents |
| `LeaveCall` | `{ appointmentId }` | Notifies the other participant the call ended |

### Server → Client events

| Event | Payload | When |
|---|---|---|
| `MessageReceived` | `{ senderId, content, messageType, sentAt }` | After a chat message is persisted |
| `IncomingCall` | `{ appointmentId, fromUserId }` | When the other participant calls `JoinCall` |
| `SignalReceived` | `{ type, payload }` | Relayed SDP/ICE from the other participant |
| `CallEnded` | `{ appointmentId }` | Other participant left or connection dropped |
| `AppointmentConfirmed` | `{ appointmentId }` | After the Stripe webhook transaction commits (Section 3.2) |
| `ConsultationSummaryReady` | `{ appointmentId, consultationId }` | After the AI pipeline transaction commits (Sequence Diagrams, Diagram 3) |
| `NotificationCreated` | `{ notificationId, type, data }` | General-purpose event mirroring any new row in `Notifications` |

**Rejection path:** if `JoinCall` fails authorization/state validation, the server does not raise `IncomingCall` for the other party — the caller instead receives a method-level error via SignalR's built-in invocation exception (no separate "rejected" event needed, since only the caller who gets rejected needs to know).

### `SendMessage` validation rules

Same rejection pattern as `JoinCall` — validation failures return a method-level SignalR invocation exception to the sender, no `MessageReceived` is broadcast:

| Rule | Detail |
|---|---|
| `content` | Required, non-empty after trimming |
| `content` max length | 2000 characters (arbitrary but concrete v1 ceiling — prevents pathological payloads before encryption/storage) |
| `messageType` | Server-set to `Text` for this method; `System` messages (e.g. "Dr. Ahmed joined the call") are created server-side only, never via a client-supplied `messageType` |
| Appointment state | Must be `Confirmed` or `InProgress` — matches the same window chat is meaningful in, consistent with `JoinCall`'s state check |
| Sender | Must be a participant of `appointmentId` (Requirements §3.4 — chat authorization rule) |

`content` is encrypted at rest once past validation (Requirements §3.4 / §5) — validation happens on the plaintext before encryption, not after.

---

## 6. What's deliberately not in this contract

Consistent with the same "don't add what v1 doesn't need" principle used throughout this series (Architecture Document, Section 15):

- No `Admin` endpoints — verification is a manual DB update in v1 (Requirements §3.1).
- No push-notification device-registration endpoint — FCM is v2 (Requirements §3.6).
- No file/attachment upload endpoints — Storage is explicitly deferred (Requirements §3.5 / Architecture §19).
- No `PATCH` partial-update semantics — `PUT` replaces the full resource everywhere in v1, which is simpler to reason about and sufficient at this scale.

---

**This closes the System Design series.** All four documents — ERD, Architecture, Sequence Diagrams, API Contract — are now mutually consistent and ready to implement against.
