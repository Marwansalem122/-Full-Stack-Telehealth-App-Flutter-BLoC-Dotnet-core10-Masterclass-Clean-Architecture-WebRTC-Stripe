# AI Telehealth Platform — Requirements Document

**Stack:** ASP.NET Core 10 (Clean Architecture, CQRS/MediatR) + Flutter (Clean Architecture, BLoC)
**Owner:** Marwan
**Status:** Draft v11 — ERD cardinalities and business rules locked — v1 open questions resolved; production-hardening concerns (concurrency, timezone, security, reliability) incorporated

---

## 1. Project Overview

A full-stack Telehealth platform that connects patients with medical consultants for online consultations. The backend is built with ASP.NET Core 10, following Clean Architecture and CQRS/MediatR conventions. The project's primary goal is educational: gaining hands-on experience with real-time communication (SignalR + WebRTC), payment integration (Stripe), and AI integration (symptom checking, consultation summarization) — implemented with production-grade concerns (concurrency, security, reliability) in mind, not just the happy path.

---

## 2. Actors

| Actor                        | Description                                                                                                                                                       |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Patient**                  | Searches for consultants, books appointments, pays for consultations, chats/video calls with the doctor, receives AI-assisted suggestions.                        |
| **Consultant (Doctor)**      | Manages availability, accepts/handles bookings, communicates with patients, receives consultation summaries. Must be verified before appearing in patient search. |
| **Admin** _(deferred to v2)_ | Oversees platform operations — not part of v1, though a minimal manual verification path exists (see 3.1).                                                        |

---

## 3. Functional Requirements (User Stories)

### 3.1 Authentication & Profiles

- As a Patient/Consultant, I want to register and log in securely, so that I can access the platform.
- As a Patient, I want to create and edit my profile (basic health info, contact details), so that consultants have context.
- As a Consultant, I want to create a professional profile (specialty, bio, credentials, timezone), so that patients can evaluate me.
- As a system, I want role-based authorization (Patient vs Consultant), so that each actor only accesses relevant endpoints.
- As a system, I want to verify resource ownership on every request (not just authentication), so that Patient A can never access Patient B's data, and Consultant A can never access Consultant B's consultations.
- As a system, I want a `ConsultantProfile.IsVerified` flag (default `false`), so that unverified consultants can complete their profile but do not appear in patient search until verified. _(v1: verification can be a manual DB update; no admin UI needed yet — the flag and the rule are what matter.)_

> **⚠️ Mutual exclusivity — a `User` must be a Patient XOR a Consultant, never both:** The ERD models `Users`→`ConsultantProfiles` and `Users`→`PatientProfiles` as two separate optional one-to-one relationships. That correctly stops a user from having _two_ Consultant Profiles, but nothing in the relationship itself stops a user from having _both_ a Consultant Profile _and_ a Patient Profile — most RDBMS (including SQL Server) don't support a plain CHECK constraint spanning two different tables.
>
> - **Primary enforcement (application layer):** `Users.Role` is set once at registration and treated as **immutable**. The `CreateConsultantProfile` command handler rejects the request unless `User.Role == Consultant`; `CreatePatientProfile` mirrors this for `Role == Patient`. As long as `Role` never changes after registration, this alone guarantees exclusivity.
> - **Optional defense-in-depth (DB layer):** A database trigger on insert into either profile table that verifies no row exists for the same `UserId` in the other table — a safety net against an application-layer bug, useful as a learning exercise even if not strictly required for v1.

### 3.2 Scheduling & Booking

- As a Consultant, I want to set my weekly availability (recurring time windows, e.g. Mon 09:00–13:00, **in my own timezone**), so that patients can only book free slots.
- As a system, I want to compute actual bookable slots as `Weekly Availability − Existing Appointments`, so that double-booking is impossible.
- As a system, I want a fixed appointment duration in v1 (**30 minutes**), so that slot calculation stays simple.
- As a Patient, I want to search consultants by specialty, so that I can find the right doctor.
- As a Patient, I want to view a consultant's available slots **converted to my local timezone**, so that times displayed are correct for me.
- As a Patient, I want to book an appointment and receive confirmation, so that I know it's secured.
- As a Consultant, I want to see my upcoming appointments, so that I can prepare.
- As a Patient/Consultant, I want to cancel or reschedule an appointment (basic rules only), so that plans can change.

**Appointment Lifecycle** — modeled as an `AppointmentStatus` enum from day one:

```
PendingPayment → Paid → Confirmed → InProgress → Completed

PendingPayment → PaymentFailed
Confirmed      → Cancelled
Confirmed      → Rescheduled
Confirmed      → NoShow
```

> **⚠️ Decide during ERD design:** `Paid` may not need to be its own `AppointmentStatus` — `Payment.Status` (Section 3.3) already tracks payment state separately. Having "paid" represented in two places risks the two getting out of sync (e.g. `Payment.Status = Paid` but `Appointment.Status` still `PendingPayment`). Consider collapsing to:
>
> ```
> PendingPayment → Confirmed → InProgress → Completed
> ```
>
> where the transition to `Confirmed` is triggered directly by the Stripe webhook confirming `Payment.Status = Paid`, keeping `Payment` as the single source of truth for payment state. Finalize this when designing the ERD, not before.

> **⚠️ Concurrency — booking race condition:** "Weekly Availability − Existing Appointments" alone is not enough to prevent double-booking if two patients book the same slot simultaneously. Enforce this at the **database level**, not just in application logic. Two viable approaches:
>
> - **Option A (simpler):** A unique constraint on `(ConsultantId, StartTime, Status)` in the `Appointments` table (excluding `Cancelled` rows), so a duplicate insert fails at the DB level.
> - **Option B:** Optimistic concurrency via `RowVersion`, or `WITH (UPDLOCK, HOLDLOCK)` when checking availability and creating the appointment inside the same transaction.
>   Decide and document the chosen approach during ERD/transaction design — this is a v1 requirement, not a nice-to-have.

- As a system, I want to prevent a Patient from having overlapping active/upcoming appointments — even across different consultants — so that a patient can't double-book themselves into two consultations at once.

> **⚠️ Patient-side overlap rule (distinct from the consultant race condition above):** The consultant-side constraint stops two _patients_ from booking the same _consultant_ slot. This is different — it stops the _same patient_ from booking two _different consultants_ at overlapping times. Since v1 fixes appointment duration to 30 minutes **and** availability slots are quantized to a fixed 30-minute grid (not offset per consultant), two overlapping appointments for the same patient will always share the exact same `ScheduledStartUtc`. That simplifies enforcement to:
>
> - A unique constraint on `(PatientId, ScheduledStartUtc)` in `Appointments`, filtered to active statuses (excluding `Cancelled`/`NoShow`/`PaymentFailed`) — same pattern as the consultant-side constraint, just on the other foreign key.
> - This check must happen inside the same transaction/lock as the consultant-side availability check (Option A/B above), not as a separate query — otherwise a race between the two checks reopens the same problem.
>   If a future version supports variable appointment durations, this simplification breaks and a true time-range overlap check (`NewStart < ExistingEnd AND NewEnd > ExistingStart`) is required instead — note this as a v2 migration risk if durations become variable.

> **⚠️ Timezone handling:** Availability set by a Cairo-based consultant and viewed by a Saudi-based patient must resolve correctly. Rule: **store all availability and appointment times in UTC**; `ConsultantProfile` carries a `TimeZoneId` (e.g. `"Africa/Cairo"`); the backend always returns UTC; the Flutter app converts to the device's local time for display. Add `TimeZoneId` to `ConsultantProfile` from v1 — retrofitting this later is painful.

### 3.3 Payments

- As a Patient, I want to pay for a consultation via Stripe at booking time, so that the appointment is confirmed only after payment.
- As a system, I want to create a Stripe PaymentIntent when a booking starts, so that payment can be tracked end-to-end.
- As a system, I want to verify payment success **only via Stripe Webhooks** (never trust the Flutter client's claim that payment succeeded), so that appointment status updates reliably and securely.
- As a system, I want to send an **Idempotency-Key** (e.g. the `AppointmentId` or a GUID stored with the appointment) on the Stripe PaymentIntent creation call, so that a network retry from the client can't create a duplicate PaymentIntent.
- As a Consultant, I want to see my earnings/payout summary, so that I can track income.

**Payment flow — ordering matters (Stripe calls should not sit inside a DB transaction, since the API call can be slow and would hold a connection/lock):**

```
1. Create Appointment (Status = PendingPayment) in DB — atomic with any related writes
2. Call Stripe to create PaymentIntent (with Idempotency-Key)
3. Update Appointment/Payment with the returned PaymentIntentId + ClientSecret
4. Stripe → Webhook → ASP.NET Core → Mark Payment = Paid → Appointment = Confirmed
```

If step 2 fails after step 1 succeeds, the Appointment stays `PendingPayment` and can be retried or expired by a cleanup job — avoids orphaned payments without needing a distributed transaction.

**Payment entity (draft):**

```
Payment
- Id
- AppointmentId
- Amount
- Currency
- StripePaymentIntentId
- StripeClientSecret   (returned to Flutter to complete payment)
- IdempotencyKey
- Status
- CreatedAt
- PaidAt
```

### 3.4 Real-time Chat & Video (WebRTC)

- As a Patient/Consultant, I want to exchange real-time messages before/during a consultation, so that we can communicate asynchronously.
- As a system, I want chat messages persisted to the database (not just delivered live), so that conversation history survives reconnects and is available later.
- As a system, I want chat message content encrypted at rest (application-layer encryption or SQL Server column-level encryption on `ChatMessage.Content`), so that patient health-related conversations aren't stored in plaintext.
- As a Patient/Consultant, I want to start a live video/audio call for the scheduled appointment, so that we can have the consultation remotely.
- As a system, I want to use SignalR **only** as the signaling layer for WebRTC (exchanging Offer/Answer/ICE Candidates/Call Events) — SignalR is not the media transport; actual audio/video flows over WebRTC peer connections.
- As a system, I want to use a STUN server for NAT traversal, and fall back to a TURN server when a direct peer connection fails, so that calls succeed across different network conditions.

> **⚠️ Chat authorization rule (not visible from the FK alone):** `ChatMessage.SenderId → Users.Id` only says _a_ valid user sent the message — it does not say that user was allowed to send it _in that appointment_. The `SenderId` FK must be checked against `Appointment.PatientId`/`Appointment.ConsultantId`: **the sender must be one of the two participants of that appointment.** This is the same resource-ownership principle from Section 3.1 applied to chat specifically — enforce it in the SignalR Hub method / command handler before persisting a message, not just at the database schema level (a plain FK constraint can't express "must be one of these two specific users").

**Chat message entity (draft):**

```
ChatMessage
- Id
- AppointmentId
- SenderId
- Content        (encrypted at rest)
- MessageType    (enum: Text, System — e.g. "Dr. Ahmed joined the call")
- SentAt
- ReadAt
```

**Chat flow:** `Flutter (sender) → SignalR → Flutter (receiver)`, while in parallel `SignalR → ASP.NET Core → Database` persists the message.

> **⚠️ SignalR scaling note:** In v1, running on a single instance is fine — no backplane needed. If this ever scales to multiple instances (e.g. Azure App Service with 2+ instances), a **Redis backplane** is required so SignalR messages reach users connected to a different instance. Worth designing the Hub with this in mind even if not implemented in v1.

### 3.5 AI Features

The AI feature is not just "call an LLM from a controller" — it's built as its own subsystem with an abstraction, provider implementation, structured prompts, validation, and failure handling. The goal is to learn production-ready AI integration patterns that transfer to any future project, not just this one.

- As a Patient, I want to describe my symptoms and get a suggested specialty, so that I know which consultant to book.
- As a system, I want to generate an automatic summary after a consultation ends, so that both parties have a record.
- As a Consultant, I want to view the AI-generated summary attached to an appointment, so that I can review it quickly.
- As a developer, I want the Application layer to depend on an `IAIService` abstraction (not directly on OpenAI/Azure SDKs), so that the AI provider can be swapped without touching business logic.
- As a developer, I want AI calls modeled as their own CQRS use cases (`AnalyzeSymptomsCommand`, `SummarizeConsultationCommand`), so that they follow the same architecture as the rest of the system.
- As a system, I want the LLM to return **structured output** (JSON: chief complaint, symptoms, key points, recommendations, red flags, disclaimer) rather than a free-text string, so that results can be validated and reliably stored/displayed.
- As a developer, I want prompts defined as versioned, structured objects (system instructions + user data + context) rather than inline string concatenation, so that prompts can evolve without breaking callers.
- As a system, I want defined failure handling for AI calls (timeout → retry, rate limit → backoff retry, invalid/malformed response → validation retry, hard failure → mark job failed), so that AI unreliability doesn't break the user experience.
- As a system, I want rate limiting on AI endpoints (e.g. `AnalyzeSymptoms` — 10 requests/minute/user via .NET's built-in rate limiting), so that a single user can't exhaust the OpenAI quota or degrade performance for others.

#### AI Abstraction Layer

```
User ──API──► AI Use Cases ──► IAIService (abstraction) ──► AI Provider
                                                              (OpenAI / Azure / etc.)
```

`IAIService` example (conceptual):

```csharp
public interface IAIService
{
    Task<SymptomAnalysisResult> AnalyzeSymptomsAsync(
        string symptoms, CancellationToken cancellationToken);

    Task<ConsultationSummaryResult> SummarizeConsultationAsync(
        ConsultationContext context, CancellationToken cancellationToken);
}
```

Implementation goes through **`Microsoft.Extensions.AI`** as the provider abstraction (rather than binding Application code directly to the OpenAI SDK), so switching providers later doesn't touch business logic.

#### Consultation Summary — Asynchronous Pipeline

Rather than making the user wait synchronously for the LLM after a call ends, the summary is generated as a background job triggered by a domain event:

```
Consultation Completed
        ↓
Publish Event (ConsultationCompleted via MediatR Notification)
        ↓
Hosted BackgroundService
        ↓
AIJob created (Status = Pending)
        ↓
Collect Consultation Context (Chat Messages + Transcript, when available)
        ↓
Build AI Prompt (versioned)
        ↓
LLM (via IAIService → Microsoft.Extensions.AI → OpenAI)
        ↓
Validate Structured Output
        ↓
Persist AISummary
        ↓
Notify Consultant
```

> **⚠️ Background job reliability:** If the server restarts mid-job, an in-memory-only `BackgroundService` loses track of it. Persist job state so it can recover:
>
> - `AIJob.Status` transitions to `Processing` with `ProcessingStartedAt` set when work begins.
> - On startup (or on a polling interval), the service picks up jobs where `Status = Pending`, **or** `Status = Processing AND ProcessingStartedAt < Now.AddMinutes(-10)` (stuck jobs get retried).
>   This makes the pipeline self-healing across restarts without needing a message queue in v1.

> **⚠️ Cardinality decision — retry vs. versioning:** `AIJob` retries (via `RetryCount`) reuse the same row and never persist an `AISummary` until one attempt succeeds — this is why `Consultation → AISummary` stays a `0..1` relationship in v1 (see Section 8 for the full rationale). If future prompt/provider experimentation needs a kept history of multiple summaries per consultation, that's a `0..*` relationship change, not something this pipeline needs to support today.

This is intentionally the same event-driven pattern used elsewhere in production systems, and teaches: domain/application events, background processing, retry, idempotency, AI failure handling, and post-completion notifications — not just "how to call an LLM."

#### Scoping: Chat vs. Voice Transcript

The **architecture** supports both chat-based and voice-based context from day one (it's not hardcoded to assume chat is the only source), but **implementation** is phased to avoid overengineering upfront:

```
                 Consultation
                      │
          ┌───────────┴───────────┐
          │                       │
      Chat Messages            Audio
          │                       │
          │                Speech-to-Text
          │                       │
          └──────────┬────────────┘
                     ↓
             Consultation Context
                     ↓
              IAIService (LLM)
                     ↓
             Structured Summary
```

- **v1:** Chat-based consultation summary, AI provider abstraction, structured output, retry/timeout/failure handling.
- **v2:** Speech-to-Text integration, voice transcript, transcript + chat combined summary.

### 3.6 Notifications _(in v1 scope, in-app only)_

- As a Patient/Consultant, I want to receive in-app notifications for key events, so that I stay informed without checking manually.
- Events to cover in v1: Appointment booked, Appointment confirmed, Appointment cancelled, Appointment starting soon, Doctor joined consultation.
- As a system, I want to deliver in-app notifications via SignalR, so that no extra infrastructure is needed for v1.
- As a Patient/Consultant, I want to see which notifications are unread, so that I can tell what's new.
- _(Push notifications via FCM remain out of scope for v1 — see Section 7.)_

---

## 4. Core Concept: Appointment vs. Consultation

These are **not** the same thing and should be modeled as separate concepts:

- **Appointment** — the booking itself (who, when, payment status, lifecycle state).
- **Consultation** — the actual session that happens during the appointment (the live call/chat interaction).

```
Appointment (1:1) Consultation
                     ├── StartedAt
                     ├── EndedAt
                     ├── Duration
                     ├── Status
                     └── Summary
```

This separation keeps WebRTC session data, chat history, and AI summaries cleanly attached to the _session_, distinct from the _booking_ record.

---

## 5. Non-Functional Requirements

- **Security:**
  - All sensitive endpoints require authentication + role-based authorization.
  - Passwords must never be stored directly (handled via ASP.NET Core Identity hashing).
  - Sensitive health data must never appear in application logs.
  - Every request must verify **resource ownership**, not just authentication — e.g. `GET /appointments/123` must confirm the caller is a participant of appointment 123, not merely that they're logged in.
  - `ChatMessage.Content` must be encrypted at rest (see 3.4).
  - Refresh tokens must never be stored as plain text. Store `TokenHash` (e.g. `SHA256`/`HMACSHA256` of the raw token) — on refresh, hash the incoming token and compare against the stored hash. If the database leaks, stored hashes alone are not directly usable as valid tokens.
- **Concurrency:** Appointment booking must be safe under concurrent requests — enforced via a DB-level unique constraint or optimistic locking (see 3.2). No double-booking under any timing scenario.
- **Performance:** Real-time chat/call should have minimal perceptible lag. AI responses should return within a few seconds (define an acceptable threshold, e.g. <5s, and handle timeouts gracefully).
- **Reliability:**
  - Stripe webhook handling must be idempotent (safe to receive duplicate events).
  - Stripe PaymentIntent creation must use an Idempotency-Key to avoid duplicate charges on client retry.
  - Background AI jobs must recover automatically after a server restart (see 3.5).
- **Scalability:** SignalR is single-instance in v1; a Redis backplane is required only if scaling beyond one instance (see 3.4).
- **Rate Limiting:** AI endpoints are rate-limited per user to control cost and load (see 3.5).
- **Caching:**
  - Consultant profile listings can be cached briefly (~5 minutes) since they change infrequently.
  - Available slots must **not** be cached beyond a few seconds (10–30s max), since they change with every booking — stale cached slots would cause booking conflicts.
- **Auditability (lightweight in v1, full audit trail in v2):** Core entities carry `CreatedAt`/`ModifiedAt` and support soft deletes from v1, so a full `AuditLog` (EntityType, EntityId, Action, UserId, Timestamp, Changes) can be added later without restructuring existing tables.
- **Maintainability:** Backend follows Clean Architecture (Data/Application/Domain/Presentation) with CQRS/MediatR, consistent with existing project conventions.

---

## 6. Scope Split — MVP vs. Later

**MVP (v1):**

- Authentication (Patient/Consultant)
- Doctor Profiles (with `TimeZoneId`, `IsVerified`) / Patient Profiles
- Availability + Appointments (30-min slots, timezone-safe, concurrency-safe)
- Stripe payments (webhook-driven, idempotent)
- Chat (persisted + encrypted at rest, SignalR real-time delivery)
- SignalR + WebRTC (with STUN/TURN)
- AI Symptom Checker (via `IAIService` abstraction, structured output, rate-limited)
- AI Consultation Summary — chat-based, async/event-driven pipeline, with retry/failure handling and restart recovery
- Basic in-app notifications (SignalR)

**Later (v2+):**

- Admin dashboard / analytics / user management (including a UI for consultant verification)
- Reviews & ratings
- Medical records / attachments
- Voice transcription for AI Summary (Speech-to-Text pipeline)
- Push notifications (FCM)
- Call recording
- Prescriptions
- Insurance integration
- Multi-language support
- Complex refund/cancellation policies
- Group video calls
- Full `AuditLog` table
- Redis SignalR backplane (only if scaling beyond one instance)
- Richer `RefreshToken` fields for multi-device session management (`DeviceId`, `UserAgent`, `IpAddress`, `RevokedReason`)

---

## 7. Out of Scope (v1)

Explicitly _not_ building initially:

- [ ] Multi-language support
- [ ] Admin dashboard / analytics
- [ ] Complex refund/cancellation policies
- [ ] Group video calls (one-to-one only)
- [ ] Insurance/claims integration
- [ ] Push notifications (FCM) — in-app only via SignalR for v1
- [ ] Speech-to-Text / voice transcription for AI Summary — architecture supports it, but implementation deferred to v2 (chat-based summary only for v1)
- [ ] Full audit log table (basic `CreatedAt`/`ModifiedAt` + soft delete support only in v1)
- [ ] SignalR Redis backplane (single-instance only in v1)

---

## 8. Decisions Made

- **AI Provider:** `Microsoft.Extensions.AI` as the abstraction layer, with **OpenAI** as the concrete provider behind it in v1. This teaches the abstraction pattern while still exercising a real provider integration.
- **Cancellation Policy (v1):** Simple fixed rule — e.g., free cancellation up to 24h before the appointment; no cancellation/penalty logic beyond that. Refine in v2 if needed.
- **Pricing Model (v1):** Flat consultation price across all consultants (no per-specialty or per-consultant variable pricing yet).
- **STUN/TURN Provider:** Google's public STUN server for v1 (free, sufficient for learning NAT traversal). Add a self-hosted **coturn** TURN server as a follow-up step to also learn the relay/infrastructure side.
- **Background Job Execution (v1):** No message queue or Hangfire yet — start with the simplest reliable pattern (Hosted BackgroundService + persisted `AIJob` state, see 3.5). Once understood end-to-end, optionally build a second version using Hangfire or a message broker to compare trade-offs — a deliberate v2 learning exercise, not a v1 requirement.
- **Consultant Verification (v1):** `IsVerified` boolean, manually flipped in the database for now — no admin UI needed until v2.
- **AI Summary cardinality — one final summary per consultation in v1:** `Consultation → 0..1 AISummary` is a deliberate decision, not just a default cardinality. `AIJob` already models retries as a single row with an incrementing `RetryCount` (not one row per attempt), so only the successful attempt ever persists an `AISummary`. If a future version needs to keep a history of summaries across different prompt versions or providers (true versioning, not retry), this relationship would need to change to `Consultation → 0..* AISummary` with an `IsCurrent`/`GeneratedAt` marker to identify the latest — that's an explicit v2 schema change, not something v1 needs to accommodate now.
- **RefreshToken fields (v1 minimal set):** `UserId`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt` — sufficient for v1. Richer fields (`DeviceId`, `UserAgent`, `IpAddress`, `RevokedReason`) are useful for multi-device session management and audit trails, but are a deliberate v2 addition, not required to ship v1 securely.
- **Booking Concurrency Control:** To be finalized during ERD design between a unique DB constraint and optimistic locking (see 3.2) — both are acceptable v1 approaches; pick one and document it.

---

## 9. Open Questions — Remaining

None outstanding for v1 at this stage. Revisit if new architectural decisions come up during implementation.

---

## 10. ERD — Entities

**Core:**

- Users
- ConsultantProfiles _(+ `TimeZoneId`, `IsVerified`, `ProfileImageUrl`)_
- PatientProfiles
- AvailabilitySlots
- Appointments _(+ `CancellationReason`, `CancelledAt`)_
- Consultations
- Payments _(+ `IdempotencyKey`, `StripeClientSecret`)_
- ChatMessages _(+ `MessageType`, encrypted `Content`)_
- AISummaries
- AIJobs _(status, retries, provider, prompt version, errors, + `ProcessingStartedAt`, `ErrorDetails`)_
- Notifications _(+ `IsRead`, `ReadAt`, `Data` JSON for deep links)_
- RefreshTokens _(store `TokenHash`, never the raw token — see security note below)_

**Deferred to v2 (do not model yet):**

- MedicalRecords
- Attachments
- Reviews
- AuditLog _(design entities with `CreatedAt`/`ModifiedAt` + soft delete now so this slots in later)_

---

## 11. High-Level Architecture

```
                        TELEHEALTH V1
                             │
        ┌────────────────────┼────────────────────┐
        │                    │                     │
     Flutter            ASP.NET Core            Services
        │                    │                     │
      BLoC             Clean Architecture           │
        │               CQRS / MediatR              │
        │                    │                      │
        └──────── REST ──────┤                      │
                             │                      │
                        SQL Server                  │
                             │                      │
                  ┌──────────┼──────────┐           │
                  │          │          │           │
               SignalR    Stripe        AI      WebRTC (STUN/TURN)
                  │      (Idempotent)   │            │
             Chat / Call            Summary       Video/Audio
              Signaling            Generation      (P2P media)
           (single instance
            in v1; Redis
            backplane if
            scaled later)
```

---

## 12. Next Steps

1. Build detailed ERD diagram from the entity list in Section 10 (relationships, keys) — including resolving the `AppointmentStatus` vs. `Payment.Status` overlap flagged in Section 3.2, and choosing the booking concurrency-control approach (unique constraint vs. optimistic locking).
2. Build sequence diagrams for the three most complex flows:
   - Booking → Payment → Confirmation flow (including the idempotency-key and non-transactional Stripe call ordering)
   - WebRTC call establishment flow (signaling → STUN → TURN fallback)
   - Consultation Completed → async AI Summary pipeline (event → BackgroundService → AIJob → IAIService → LLM → validation → persistence → notification), including stuck-job recovery
3. Design `IAIService` interface and the two initial use cases (`AnalyzeSymptoms`, `SummarizeConsultation`) before writing any provider-specific code.
4. Decide the chat encryption approach (application-layer vs. SQL Server column-level encryption) before implementing `ChatMessage`.
