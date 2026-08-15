# AI Telehealth Platform — Requirements Document

**Stack:** ASP.NET Core 10 (Clean Architecture, CQRS/MediatR) + Flutter (Clean Architecture, BLoC)
**Owner:** Marwan
**Status:** Draft v14 — fixed booking-retry logic bug, webhook made atomic/transactional, StripeClientSecret decision resolved, stale ERD-pending language removed, Next Steps updated

---

## 1. Project Overview

A full-stack Telehealth platform that connects patients with medical consultants for online consultations. The backend is built with ASP.NET Core 10, following Clean Architecture and CQRS/MediatR conventions. The project's primary goal is educational: gaining hands-on experience with real-time communication (SignalR + WebRTC), payment integration (Stripe), and AI integration (symptom checking, consultation summarization) — implemented with production-grade concerns (concurrency, security, reliability) in mind, not just the happy path.

---

## 2. Actors

| Actor | Description |
|---|---|
| **Patient** | Searches for consultants, books appointments, pays for consultations, chats/video calls with the doctor, receives AI-assisted suggestions. |
| **Consultant (Doctor)** | Manages availability, accepts/handles bookings, communicates with patients, receives consultation summaries. Must be verified before appearing in patient search. |
| **Admin** *(deferred to v2)* | Oversees platform operations — not part of v1, though a minimal manual verification path exists (see 3.1). |

---

## 3. Functional Requirements (User Stories)

### 3.1 Authentication & Profiles
- As a Patient/Consultant, I want to register and log in securely, so that I can access the platform.
- As a Patient, I want to create and edit my profile (basic health info, contact details), so that consultants have context.
- As a Consultant, I want to create a professional profile (specialty, bio, credentials, timezone), so that patients can evaluate me.
- As a system, I want role-based authorization (Patient vs Consultant), so that each actor only accesses relevant endpoints.
- As a system, I want to verify resource ownership on every request (not just authentication), so that Patient A can never access Patient B's data, and Consultant A can never access Consultant B's consultations.
- As a system, I want a `ConsultantProfile.IsVerified` flag (default `false`), so that unverified consultants can complete their profile but do not appear in patient search until verified. *(v1: verification can be a manual DB update; no admin UI needed yet — the flag and the rule are what matter.)*

> **⚠️ Mutual exclusivity — a `User` must be a Patient XOR a Consultant, never both:** The ERD models `Users`→`ConsultantProfiles` and `Users`→`PatientProfiles` as two separate optional one-to-one relationships. That correctly stops a user from having *two* Consultant Profiles, but nothing in the relationship itself stops a user from having *both* a Consultant Profile *and* a Patient Profile — most RDBMS (including SQL Server) don't support a plain CHECK constraint spanning two different tables.
> - **Primary enforcement (application layer):** `Users.Role` is set once at registration and treated as **immutable**. The `CreateConsultantProfile` command handler rejects the request unless `User.Role == Consultant`; `CreatePatientProfile` mirrors this for `Role == Patient`. As long as `Role` never changes after registration, this alone guarantees exclusivity.
> - **Optional defense-in-depth (DB layer):** A database trigger on insert into either profile table that verifies no row exists for the same `UserId` in the other table — a safety net against an application-layer bug, useful as a learning exercise even if not strictly required for v1.

### 3.2 Scheduling & Booking
- As a Consultant, I want to set my weekly availability (recurring time windows, e.g. Mon 09:00–13:00, **in my own timezone**), so that patients can only book free slots.
- As a system, I want to compute actual bookable slots as `Weekly Availability − Existing Appointments`, so that double-booking is impossible.
- As a system, I want a fixed appointment duration in v1 (**30 minutes**), so that slot calculation stays simple.
- As a Patient, I want to search consultants by specialty, so that I can find the right doctor.
- As a Patient, I want to view a consultant's available slots **converted to my local timezone**, so that times displayed are correct for me.
- As a Patient, I want to book an appointment and receive confirmation, so that I know it's secured.
- As a system, I want `POST /appointments` itself to accept a client-generated **Idempotency-Key** header, so that a lost response (network dies after the appointment is created but before Flutter receives the ID) can't cause the client to accidentally create a second appointment on retry.
- As a Consultant, I want to see my upcoming appointments, so that I can prepare.
- As a Patient/Consultant, I want to cancel or reschedule an appointment (basic rules only), so that plans can change.

**Appointment Lifecycle** — modeled as an `AppointmentStatus` enum from day one. **Finalized** (see rationale below — `Paid` is not part of this enum):

```
PendingPayment → Confirmed → InProgress → Completed

PendingPayment → PaymentFailed
Confirmed      → Cancelled
Confirmed      → Rescheduled
Confirmed      → NoShow
```

> **✅ Resolved: no separate `Paid` status on Appointment.** `Payment.Status` (Section 3.3) is the single source of truth for payment state. The Stripe webhook confirming `Payment.Status = Paid` transitions the Appointment directly to `Confirmed` in the same step — there is exactly one place that answers "was this paid," not two states that could drift out of sync.

> **✅ Resolved: booking concurrency.** The v1 decision is finalized: **filtered unique indexes**, not optimistic locking:
> - `UNIQUE (ConsultantId, ScheduledStartUtc) WHERE Status NOT IN (Cancelled, NoShow, PaymentFailed)` on the `Appointments` table, so a duplicate insert for an *active* booking fails at the DB level, while a cancelled appointment never blocks the slot.
> This is used for both the consultant-side and patient-side constraints (see below) — see Section 8 for the full rationale.

- As a system, I want to prevent a Patient from having overlapping active/upcoming appointments — even across different consultants — so that a patient can't double-book themselves into two consultations at once.

> **⚠️ Patient-side overlap rule (distinct from the consultant race condition above):** The consultant-side constraint stops two *patients* from booking the same *consultant* slot. This is different — it stops the *same patient* from booking two *different consultants* at overlapping times. Since v1 fixes appointment duration to 30 minutes **and** availability slots are quantized to a fixed 30-minute grid (not offset per consultant), two overlapping appointments for the same patient will always share the exact same `ScheduledStartUtc`. That simplifies enforcement to:
> - `UNIQUE (PatientId, ScheduledStartUtc) WHERE Status NOT IN (Cancelled, NoShow, PaymentFailed)` on `Appointments` — same filtered-index pattern as the consultant-side constraint, just on the other foreign key.
> - This check must happen inside the same transaction/lock as the consultant-side availability check (Option A/B above), not as a separate query — otherwise a race between the two checks reopens the same problem.
> If a future version supports variable appointment durations, this simplification breaks and a true time-range overlap check (`NewStart < ExistingEnd AND NewEnd > ExistingStart`) is required instead — note this as a v2 migration risk if durations become variable.

> **⚠️ Timezone handling:** Availability set by a Cairo-based consultant and viewed by a Saudi-based patient must resolve correctly. Rule: **store all availability and appointment times in UTC**; `ConsultantProfile` carries a `TimeZoneId` (e.g. `"Africa/Cairo"`); the backend always returns UTC; the Flutter app converts to the device's local time for display. Add `TimeZoneId` to `ConsultantProfile` from v1 — retrofitting this later is painful.

> **⚠️ Booking idempotency is a separate concern from Stripe idempotency — both are needed.** The Stripe `Idempotency-Key` (Section 3.3) only protects the *PaymentIntent creation* call. It does nothing for the earlier step: if the network dies after `POST /appointments` successfully creates the Appointment but before Flutter receives the response, Flutter never learns the `AppointmentId` — and a naive retry of `POST /appointments` creates a **second, duplicate appointment**.
> - `POST /appointments` itself must accept a client-generated `Idempotency-Key` header (a GUID Flutter generates once per booking attempt and reuses on retry).
> - `Appointment.IdempotencyKey` is `UNIQUE`. On a retry with the same key, the API looks up the existing appointment — but **finding an existing appointment is not automatically "return it and stop."** The correct behavior depends on how far the *previous* attempt got:
>   - If a `PaymentIntent` already exists for it → return the existing result (client secret included) — the original attempt succeeded past that point, nothing to redo.
>   - If it's `PendingPayment` with **no** `PaymentIntent` yet (the original attempt died *before* reaching Stripe) → **resume** the flow and attempt `PaymentIntent` creation now, rather than just returning a half-finished result.
>   Treating "key already exists" as always meaning "just return it" would silently strand a booking that failed before ever reaching Stripe — the retry has to be able to pick up where the previous attempt actually left off.
> - The *same* key then flows through to the Stripe `PaymentIntent` call (`Payment.IdempotencyKey = Appointment.IdempotencyKey`) — one key, reused end-to-end, rather than generating a second, unrelated key at the Stripe step.

### 3.3 Payments
- As a Patient, I want to pay for a consultation via Stripe at booking time, so that the appointment is confirmed only after payment.
- As a system, I want to create a Stripe PaymentIntent when a booking starts, so that payment can be tracked end-to-end.
- As a system, I want to verify payment success **only via Stripe Webhooks** (never trust the Flutter client's claim that payment succeeded), so that appointment status updates reliably and securely.
- As a system, I want to send an **Idempotency-Key** on the Stripe PaymentIntent creation call — the *same* key as `Appointment.IdempotencyKey` (Section 3.2), not a separately generated one — so that a network retry from the client can't create a duplicate PaymentIntent.
- As a Consultant, I want to see my earnings/payout summary, so that I can track income.

**Payment flow — ordering matters (Stripe calls should not sit inside a DB transaction, since the API call can be slow and would hold a connection/lock):**
```
1. Create Appointment (Status = PendingPayment) in DB — atomic with any related writes
2. Call Stripe to create PaymentIntent (with Idempotency-Key)
3. Update Appointment/Payment with the returned PaymentIntentId + ClientSecret
4. Stripe → Webhook → ASP.NET Core → Mark Payment = Paid → Appointment = Confirmed
```
If step 2 fails after step 1 succeeds, the Appointment stays `PendingPayment` and can be retried (resuming from step 2, per the idempotency-key logic above) or expired by a cleanup job — avoids orphaned payments without needing a distributed transaction.

> **⚠️ Webhook idempotency needs an atomic mechanism, not a "check then insert" race:** two near-simultaneous deliveries of the same webhook event could both pass a `SELECT ... WHERE StripeEventId = ?` check before either has inserted its row — a plain "check, then process, then record" sequence has a race window. Make the **insert itself** the atomicity boundary, not a separate check beforehand:
> ```
> StripeWebhookEvents
> - Id
> - StripeEventId   UNIQUE
> - EventType
> - ReceivedAt
> - ProcessedAt
> ```
> Flow: verify signature → **attempt to `INSERT` the `StripeEventId` row first** → if the insert fails (unique constraint violation), another request already claimed this event — return `200` without reprocessing → if the insert succeeds, this request has exclusively claimed the event and proceeds to process it. The `UNIQUE` constraint is what makes this atomic; a prior `SELECT` check does not.
> **Process the whole thing as one transaction:** `INSERT StripeWebhookEvent` + `Payment.Status = Paid` + `Appointment.Status = Confirmed` + `Create Notification`, committed together. If any step fails, the whole webhook attempt rolls back and Stripe's automatic retry will redeliver — rather than risking a partial state like `Payment = Paid` with `Appointment` still `PendingPayment`, or a `Confirmed` appointment with no notification ever created. `SignalR` delivery happens *after* the transaction commits, consistent with "persist first, notify second."

> **⚠️ PaymentIntent creation failure is a named branch, not just an implied edge case:** if the call to Stripe in step 2 fails outright (network error, Stripe downtime), the Appointment remains `PendingPayment` and the API returns a clear "payment initialization failed" response to the client — the client can retry the booking request (safe, thanks to the Idempotency-Key) rather than being left in an ambiguous state. A `PendingPayment` appointment that never completes payment should eventually be expired by a cleanup job (exact expiry window is a v1 implementation detail, not an open design question).

**Payment entity (draft):**
```
Payment
- Id
- AppointmentId
- Amount
- Currency
- StripePaymentIntentId
- IdempotencyKey
- Status
- CreatedAt
- PaidAt
```

> **✅ Resolved: `StripeClientSecret` is not persisted.** It's returned to Flutter directly at creation time and never stored in `Payment` — there's no v1 use case that needs the backend to retrieve it later (e.g. no server-side payment-resumption flow). Being sensitive, the fewer places it's stored, the smaller the exposure surface; not persisting it also means one less thing to ensure never leaks into logs.
>
> **⚠️ Retrieval behavior on retry (must be documented in API Contract):** Because `StripeClientSecret` is not persisted, a retry with an existing `Idempotency-Key` that already has a `PaymentIntentId` cannot simply return the stored record — the `ClientSecret` is gone. Instead, the backend must:
> ```
> Existing Appointment found
>     ↓
> PaymentIntentId exists?
>     ↓ YES
> Retrieve PaymentIntent from Stripe (using the stored PaymentIntentId)
>     ↓
> Extract ClientSecret from Stripe response
>     ↓
> Return ClientSecret to Flutter
> ```
> This is a single Stripe API call (`GET /v1/payment_intents/{id}`) and is safe because the `PaymentIntent` was created by this same backend (same Stripe account). This retrieval step must be explicitly documented in the API Contract for `POST /appointments` so the retry path is fully specified.

### 3.4 Real-time Chat & Video (WebRTC)
- As a Patient/Consultant, I want to exchange real-time messages before/during a consultation, so that we can communicate asynchronously.
- As a system, I want chat messages persisted to the database (not just delivered live), so that conversation history survives reconnects and is available later.
- As a system, I want chat message content encrypted at rest (application-layer encryption or SQL Server column-level encryption on `ChatMessage.Content`), so that patient health-related conversations aren't stored in plaintext.
- As a Patient/Consultant, I want to start a live video/audio call for the scheduled appointment, so that we can have the consultation remotely.
- As a system, I want to use SignalR **only** as the signaling layer for WebRTC (exchanging Offer/Answer/ICE Candidates/Call Events) — SignalR is not the media transport; actual audio/video flows over WebRTC peer connections.
- As a system, I want to use a STUN server for NAT traversal, and fall back to a TURN server when a direct peer connection fails, so that calls succeed across different network conditions.

> **⚠️ Chat authorization rule (not visible from the FK alone):** `ChatMessage.SenderId → Users.Id` only says *a* valid user sent the message — it does not say that user was allowed to send it *in that appointment*. The `SenderId` FK must be checked against `Appointment.PatientId`/`Appointment.ConsultantId`: **the sender must be one of the two participants of that appointment.** This is the same resource-ownership principle from Section 3.1 applied to chat specifically — enforce it in the SignalR Hub method / command handler before persisting a message, not just at the database schema level (a plain FK constraint can't express "must be one of these two specific users").

> **⚠️ Joining a call needs an appointment-status check, not just a participant check:** being a legitimate participant of the appointment (resource ownership) is necessary but not sufficient — a participant could still try to join a `Cancelled` or already-`Completed` appointment. `JoinCall` must validate both: (1) caller is a participant, and (2) `Appointment.Status` is in an allowed state (`Confirmed`/`InProgress`) before permitting the call to start. Exact allowed-status list is a v1 implementation detail, not an open design question — the principle (validate status, not just identity) is what matters here.

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

Rather than making the user wait synchronously for the LLM after a call ends, the summary is generated as background work, independent of the request/response cycle:

```
CompleteConsultation Command
        ↓
DB Transaction
  ├── Consultation.Status = Completed
  └── AIJob created (Status = Pending)
        ↓
Commit (API returns immediately — no waiting on AI)
        ↓
Hosted BackgroundService independently polls for Pending jobs
        ↓
Atomic job claim (Status = Pending → Processing, in one update)
        ↓
Collect Consultation Context (Chat Messages — v1; Transcript in v2, when available)
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

> **✅ Resolved: `AIJob` is created transactionally, not via a published event.** `Consultation.Status = Completed` and `AIJob` creation happen in the **same database transaction** as the `CompleteConsultation` command — not through a domain event that a separate handler reacts to. This is simpler and more reliable for v1: an event-based approach only pays off with an Outbox Pattern (guaranteeing the event is never lost between commit and publish), which is explicitly out of scope for v1 (no message broker, no Outbox). The `BackgroundService` polls the database directly for `Pending` jobs rather than reacting to a published event.

> **⚠️ Background job reliability:** If the server restarts mid-job, an in-memory-only `BackgroundService` loses track of it. Persist job state so it can recover:
> - `AIJob.Status` transitions to `Processing` with `ProcessingStartedAt` set when work begins — and this claim must be **atomic** (a single conditional update, e.g. `UPDATE ... SET Status='Processing' WHERE Status='Pending'`, checking rows affected), so that if the service ever runs as more than one instance, two workers can never both claim the same job.
> - On startup (or on a polling interval), the service picks up jobs where `Status = Pending`, **or** `Status = Processing AND ProcessingStartedAt < Now.AddMinutes(-10)` (stuck jobs get retried).
> This makes the pipeline self-healing across restarts without needing a message queue in v1.

> **⚠️ Retry policy — bounded, not infinite:** `RetryCount` must be checked against a `MaxRetries` ceiling (exact numbers aren't critical for v1 — e.g. 3 attempts with increasing backoff between them) before requeuing as `Pending`. Once `RetryCount` reaches the max, the job moves to a terminal `Failed` status rather than retrying forever.

> **⚠️ Duplicate-summary protection:** if saving an `AISummary` fails after a successful AI call, a subsequent retry could call the AI provider again and attempt to persist a second summary for the same consultation. Add `UNIQUE (ConsultationId)` on `AISummaries` as a hard backstop — consistent with the `0..1` cardinality decision below, this makes that cardinality enforced by the database, not just assumed by application logic.

> **⚠️ Cardinality decision — retry vs. versioning:** `AIJob` retries (via `RetryCount`) reuse the same row and never persist an `AISummary` until one attempt succeeds — this is why `Consultation → AISummary` stays a `0..1` relationship in v1 (see Section 8 for the full rationale). If future prompt/provider experimentation needs a kept history of multiple summaries per consultation, that's a `0..*` relationship change, not something this pipeline needs to support today.

This is intentionally the same background-processing pattern used elsewhere in production systems, and teaches: transactional job creation, atomic claiming, retry with bounded backoff, idempotency, AI failure handling, and post-completion notifications — not just "how to call an LLM."

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

### 3.6 Notifications *(in v1 scope, in-app only)*
- As a Patient/Consultant, I want to receive in-app notifications for key events, so that I stay informed without checking manually.
- Events to cover in v1: Appointment booked, Appointment confirmed, Appointment cancelled, Appointment starting soon, Doctor joined consultation.
- As a system, I want to deliver in-app notifications via SignalR, so that no extra infrastructure is needed for v1.
- As a Patient/Consultant, I want to see which notifications are unread, so that I can tell what's new.
- *(Push notifications via FCM remain out of scope for v1 — see Section 7.)*

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

This separation keeps WebRTC session data, chat history, and AI summaries cleanly attached to the *session*, distinct from the *booking* record.

---

## 5. Non-Functional Requirements

- **Security:**
  - All sensitive endpoints require authentication + role-based authorization.
  - Passwords must never be stored directly (handled via ASP.NET Core Identity hashing).
  - Sensitive health data must never appear in application logs.
  - Every request must verify **resource ownership**, not just authentication — e.g. `GET /appointments/123` must confirm the caller is a participant of appointment 123, not merely that they're logged in.
  - `ChatMessage.Content` must be encrypted at rest (see 3.4).
  - Refresh tokens must never be stored as plain text. Store `TokenHash` (e.g. `SHA256`/`HMACSHA256` of the raw token) — on refresh, hash the incoming token and compare against the stored hash. If the database leaks, stored hashes alone are not directly usable as valid tokens.
- **Concurrency:** Appointment booking must be safe under concurrent requests — enforced via filtered unique indexes (see 3.2 and Section 8). No double-booking under any timing scenario.
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
- AI Consultation Summary — chat-based, async pipeline with transactional job creation and DB polling, retry/failure handling and restart recovery
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

Explicitly *not* building initially:

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
- **Booking Concurrency Control:** ✅ Finalized (see Section 3.2 and the Sequence Diagrams document) — **filtered unique indexes**, not optimistic locking:
  ```sql
  UNIQUE (ConsultantId, ScheduledStartUtc) WHERE Status NOT IN (Cancelled, NoShow, PaymentFailed)
  UNIQUE (PatientId, ScheduledStartUtc)    WHERE Status NOT IN (Cancelled, NoShow, PaymentFailed)
  ```

---

## 9. Open Questions — Remaining

None outstanding for v1 at this stage. Revisit if new architectural decisions come up during implementation.

---

## 10. ERD — Entities

**Core:**
- Users
- ConsultantProfiles *(+ `TimeZoneId`, `IsVerified`, `ProfileImageUrl`)*
- PatientProfiles
- AvailabilitySlots
- Appointments *(+ `CancellationReason`, `CancelledAt`, `IdempotencyKey` UNIQUE — see booking idempotency note in Section 3.2)*
- Consultations
- Payments *(+ `IdempotencyKey`; `StripeClientSecret` deliberately NOT persisted — see Section 3.3)*
- ChatMessages *(+ `MessageType`, encrypted `Content`)*
- AISummaries *(`ConsultationId` UNIQUE — backstops the `0..1` cardinality against a retry-after-partial-failure race)*
- AIJobs *(status, retries, provider, prompt version, errors, + `ProcessingStartedAt`, `ErrorDetails`)*
- Notifications *(+ `IsRead`, `ReadAt`, `Data` JSON for deep links)*
- RefreshTokens *(store `TokenHash`, never the raw token — see security note below)*
- StripeWebhookEvents *(`StripeEventId` UNIQUE — makes webhook idempotency concrete, see Section 3.3)*

**Deferred to v2 (do not model yet):**
- MedicalRecords
- Attachments
- Reviews
- AuditLog *(design entities with `CreatedAt`/`ModifiedAt` + soft delete now so this slots in later)*

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

**Completed (System Design series):**
1. ✅ ERD — finalized (entities, relationships, filtered unique indexes, `AppointmentStatus` vs. `Payment.Status` resolved)
2. ✅ Architecture Diagram + Deployment Diagram — finalized
3. ✅ Sequence Diagrams — finalized for the three complex flows (Booking → Payment → Confirmation, WebRTC call establishment, Consultation → AI Summary)

**Remaining before implementation:**
4. Design the **API Contract** — endpoint list, request/response shapes, validation, authorization, and status codes, building directly on the finalized sequence diagrams.
5. Design the **`IAIService` interface contract** — `AnalyzeSymptomsAsync` and `SummarizeConsultationAsync` signatures, input/output DTOs, and provider-agnostic abstractions. This is an internal implementation contract, not part of the public API surface.
6. Decide the **chat encryption approach** (application-layer vs. SQL Server column-level encryption) before implementing `ChatMessage`.
7. **Implementation** — begin coding against the finalized contracts above.
