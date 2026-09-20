# AI Telehealth Platform — Requirements Document

**Stack:** ASP.NET Core 10 (Clean Architecture, CQRS/MediatR) + Flutter (Clean Architecture, BLoC) + Native SDK Layer (Kotlin/Android, Swift/iOS via Platform Channels)
**Owner:** Marwan
**Status:** Draft v21 — added a deferred v2 note on app version gating / staged rollout strategy (Play/App Store + Shorebird), scoped to activate only if a concrete operational need arises; fixed a stale duplicate "Push notifications (FCM)" entry left in the Later (v2+) list after FCM was already moved to v1 in the Out of Scope section.

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
- As a Patient/Consultant, I want to verify my email address during registration, so that the platform can trust my identity and I can recover my account.
- As a Patient/Consultant, I want to reset my password if I forget it, so that I can regain access without admin intervention.
- As a Patient/Consultant, I want to change my password while logged in, so that I can secure my account if I suspect compromise.
- As a system, I want to enforce a strong password policy (minimum length, complexity, common-password rejection), so that weak passwords don't compromise accounts.
- As a system, I want to lock accounts after repeated failed login attempts, so that brute-force attacks are ineffective.
- As a system, I want to protect against user enumeration attacks (same response regardless of email existence), so that attackers can't build a user list.
- As a Patient, I want to create and edit my health profile (height, weight, blood type, allergies, chronic conditions, current medications), so that consultants have structured, actionable context.
- As a Patient, I want to update my contact details, so that the platform and consultants can reach me.
- As a Consultant, I want to create a professional profile (specialty, bio, credentials, timezone), so that patients can evaluate me.
- As a system, I want role-based authorization (Patient vs Consultant), so that each actor only accesses relevant endpoints.
- As a system, I want to verify resource ownership on every request (not just authentication), so that Patient A can never access Patient B's data, and Consultant A can never access Consultant B's consultations.
- As a system, I want a `ConsultantProfile.IsVerified` flag (default `false`), so that unverified consultants can complete their profile but do not appear in patient search until verified. *(v1: verification can be a manual DB update; no admin UI needed yet — the flag and the rule are what matter.)*
- As a system, I want email verification to be mandatory before allowing bookings or payments, so that fake accounts can't abuse the platform.

> **⚠️ Mutual exclusivity — a `User` must be a Patient XOR a Consultant, never both:** The ERD models `Users`→`ConsultantProfiles` and `Users`→`PatientProfiles` as two separate optional one-to-one relationships. That correctly stops a user from having *two* Consultant Profiles, but nothing in the relationship itself stops a user from having *both* a Consultant Profile *and* a Patient Profile — most RDBMS (including SQL Server) don't support a plain CHECK constraint spanning two different tables.
> - **Primary enforcement (application layer):** `Users.Role` is set once at registration and treated as **immutable**. The `CreateConsultantProfile` command handler rejects the request unless `User.Role == Consultant`; `CreatePatientProfile` mirrors this for `Role == Patient`. As long as `Role` never changes after registration, this alone guarantees exclusivity.
> - **Optional defense-in-depth (DB layer):** A database trigger on insert into either profile table that verifies no row exists for the same `UserId` in the other table — a safety net against an application-layer bug, useful as a learning exercise even if not strictly required for v1.
>
> **⚠️ Known limitation (v1):** A consultant who gets sick and wants to book an appointment with another doctor **cannot** do so using the same account. They would need a separate email/phone to register as a patient. This is a deliberate v1 simplification. v2 can relax this constraint by:
> - Allowing users to have *both* profiles (Patient + Consultant)
> - Or creating a "switch role" feature
> - Or adding a "book for myself" option in the consultant dashboard that creates a temporary patient context
> For v1, the XOR rule stands to keep the authorization model simple.
>
> **✅ Resolved: a third `UserRole` value, `Admin`, exists alongside `Patient`/`Consultant` — explicitly exempt from the XOR rule above.** An Admin has **neither** a `ConsultantProfile` nor a `PatientProfile` — the XOR rule (Patient *or* Consultant, never both) only ever applied between those two; it says nothing about Admin, which isn't part of that either/or in the first place. Admin accounts are **never self-registered** — `POST /auth/register` rejects any `role` value other than `"Patient"`/`"Consultant"` (the validator's `Must(r => r is "Patient" or "Consultant")` check makes `Admin` structurally unreachable through that endpoint), and an Admin `User` row is provisioned directly in the database (Section 3.3.2). `RegisterCommandHandler`'s profile-creation branch (Patient → `PatientProfile`, else → `ConsultantProfile`) is therefore never exercised for an Admin, since no code path can produce one.

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
- As a Patient/Consultant, I want an automatic refund when I cancel more than 24 hours before the appointment, so that I'm not charged for appointments I couldn't attend.
- As a Consultant, I want cancelled appointments (by either party) to be automatically refunded when eligible, so that patients trust the platform and my earnings reflect only completed consultations.

**Appointment Lifecycle** — modeled as an `AppointmentStatus` enum from day one. **Finalized** (see rationale below — `Paid` is not part of this enum):

```
PendingPayment → Confirmed → InProgress → Completed

PendingPayment → PaymentFailed
Confirmed      → Cancelled
Confirmed      → Rescheduled
Confirmed      → NoShow
```

> **✅ Resolved: no separate `Paid` status on Appointment.** `Payment.Status` (Section 3.3) is the single source of truth for payment state. The Stripe webhook confirming `Payment.Status = Paid` transitions the Appointment directly to `Confirmed` in the same step — there is exactly one place that answers "was this paid," not two states that could drift out of sync.
>
> **✅ Resolved: refund flow for v1.** When a `Confirmed` appointment is cancelled more than 24 hours before the scheduled time, the system automatically initiates a Stripe refund. `Payment.RefundStatus` tracks the refund lifecycle (`None` → `Pending` → `Succeeded`/`Failed`). The Stripe `charge.refunded` webhook updates `RefundStatus = Succeeded` and `RefundedAt`. Cancellations within 24h do not trigger refunds (payment is kept). This rule applies equally to patient-initiated and consultant-initiated cancellations.

### 3.2.1 Refund Policy — Explicit Rules

The following table is the single source of truth for when a refund is issued. This is a **business rule**, not an implementation detail.

| Scenario | Who Cancels | Refund? | Reason |
|---|---|---|---|
| Cancel > 24h before appointment | Patient | ✅ **Full refund** | Enough notice given |
| Cancel > 24h before appointment | Consultant | ✅ **Full refund** | Enough notice given |
| Cancel ≤ 24h before appointment | Patient | ❌ **No refund** | Late cancellation penalty |
| Cancel ≤ 24h before appointment | Consultant | ❌ **No refund** | Late cancellation penalty |
| No-show (patient never joins) | System (auto) | ❌ **No refund** | Patient forfeits payment |
| No-show (consultant never joins) | System (auto) | ✅ **Full refund** | Consultant at fault |
| Appointment completed successfully | — | ❌ **No refund** | Service delivered |

**Key principles:**
- **Symmetry:** Patient and consultant have identical cancellation rights. Neither party is privileged.
- **24-hour window:** The cutoff is strict. `ScheduledStartUtc - 24h` is the boundary; one minute over = no refund.
- **No partial refunds:** v1 only supports full refund or no refund. Partial refunds (e.g., 50%) are deferred to v2.
- **Refund initiation is synchronous:** The Stripe API call happens during the `POST /appointments/{id}/cancel` request. The user receives `RefundStatus = Pending` immediately.
- **Refund confirmation is asynchronous:** The actual money movement is confirmed by Stripe webhook (`charge.refunded`). This may take minutes to hours depending on the payment method and Stripe processing.
- **Earnings exclusion:** `GET /consultants/me/earnings` excludes all payments where `RefundStatus = Succeeded`. A refunded appointment never counts as income.

### 3.2.2 No-Show Detection — Background Job

> **⚠️ Critical gap closed:** The `NoShow` status in the `AppointmentStatus` enum was previously undefined — no trigger condition was specified. The following rules are now the single source of truth for no-show detection.

**Grace period:** 15 minutes after `ScheduledStartUtc`. If the call has not started by then, the system evaluates no-show.

**Detection mechanism:**
1. `JoinCall` SignalR hub method records a timestamp on the `Consultation` entity:
   - `Consultation.PatientJoinedAt` — set when the patient calls `JoinCall`
   - `Consultation.ConsultantJoinedAt` — set when the consultant calls `JoinCall`
   - `Consultation.StartedAt` — set when the *first* participant successfully joins (i.e., the call transitions to `InProgress`)
2. A `Hosted BackgroundService` (`NoShowDetectionService`) runs every 5 minutes.
3. It queries: `Appointments` where `Status = Confirmed` AND `ScheduledStartUtc + 15 minutes <= Now` AND `Consultation.StartedAt IS NULL`.
4. For each qualifying appointment, it determines fault:

| `PatientJoinedAt` | `ConsultantJoinedAt` | Fault | `Appointment.Status` | Refund? |
|---|---|---|---|---|
| Null | Has value | **Patient no-show** | `NoShow` | ❌ No refund |
| Has value | Null | **Consultant no-show** | `NoShow` | ✅ Full refund |
| Null | Null | **Mutual no-show** | `NoShow` | ❌ **No refund — patient forfeits** |
| Has value | Has value | *Should not happen* — `StartedAt` would be set | — | — |

**Actions taken by the background job (single transaction):**
1. `Appointment.Status = NoShow`
2. `Consultation.Status = PatientNoShow` / `ConsultantNoShow` / `MutualNoShow`
3. Create `Notification` for both parties ("You were marked as no-show for your appointment at ...")
4. If consultant no-show: initiate Stripe refund (`Payment.RefundStatus = Pending`, call Stripe API)
5. Commit

#### Mutual No-Show — Explicit Decision

> **⚠️ This is a deliberate v1 decision, not a gap.**

When **neither** party joins the call within 15 minutes, the system does **not** leave the appointment in an ambiguous state. The rule is definitive:

```
Mutual No-Show → Appointment.Status = NoShow, Payment kept (no refund)
```

**Why patient forfeits (not split, not platform liability):**
- v1 has **no partial refund support** — only full refund or no refund.
- The consultant did not fail alone (they also didn't join), so they don't qualify for the "consultant no-show = refund" rule.
- The patient did not fail alone (they also didn't join), so they don't qualify for the "patient no-show = forfeit" rule as a clear-cut case.
- In the absence of a clear fault, **the party that paid (patient) bears the loss**. This is the simplest, most defensible rule for v1.
- The platform never absorbs the cost in v1 — no "platform liability" concept exists yet.

**If this feels unfair:** that's a v2 concern. v2 can introduce:
- Partial refunds (e.g., 50/50 split)
- Platform credit/voucher for mutual no-shows
- Admin review of mutual no-shows
- Reputation penalties for both parties

But v1 must have a **deterministic rule** — not a hanging state. The background job commits `MutualNoShow` + no refund immediately.

**Late join protection:**
- If `Appointment.Status = NoShow`, `JoinCall` rejects with `409 Conflict` ("This appointment has been marked as no-show and cannot be joined").
- This prevents a participant from joining 20 minutes late and confusing the state.

**Why 15 minutes?**
- Short enough to resolve the appointment promptly (patient isn't left waiting indefinitely).
- Long enough to account for minor delays (network issues, app startup time, notification delay).
- v1 decision — adjustable in v2 based on analytics.

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
- As a Consultant, I want to see my earnings/payout summary (excluding refunded appointments, net of platform commission), so that I can track actual income and pending payouts.

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

### 3.3.1 Payout & Commission Policy — Explicit Rules

> **⚠️ Critical gap closed, then re-closed properly:** the first version of this section assumed Stripe Connect works everywhere. It doesn't — **Stripe Connect does not support payouts to Egypt** (as of this writing), and the platform's actual go-to-market is Egypt + the Arab world first, with global expansion later. A commission policy that only works in Stripe-supported countries is not a v1-ready policy for this project. The rules below account for both paths.

**Payment provider is per-consultant, chosen by country — not a single global choice:**

```
Booking / Payment Command
        ↓
   IPaymentProvider (abstraction)
        ↓
ConsultantProfile.Country determines the concrete provider:
        │
        ├── Stripe-supported country (Saudi Arabia, UAE, US, EU, ...)
        │       → StripeConnectPaymentProvider
        │       → automatic split + weekly automatic bank payout
        │
        └── Stripe-unsupported country (Egypt, ...)
                → RegionalPaymentProvider (e.g. Paymob or PayTabs for card/wallet collection)
                → platform collects the full amount into its own merchant account
                → consultant's 80% share is recorded as a PayoutLedgerEntry (owed, not yet paid)
                → payout settled manually via bank transfer, approved through the Admin Dashboard (see Section 3.3.2)
```

> **⚠️ `ConsultantProfile` needs a `Country` field — this did not previously exist anywhere in the ERD.** It's required for this routing decision and is a real gap independent of the payment discussion (patient search should arguably also filter/display by country). Add it as a required field at consultant profile completion, alongside `TimeZoneId`.

**Commission structure (v1) — unchanged split, corrected calculation basis:**
| Party | Percentage | Notes |
|---|---|---|
| **Consultant** | 80% | Stripe path: minus Stripe's processing fee. Regional path: minus the regional gateway's processing fee (varies by provider — Paymob/PayTabs fees are not identical to Stripe's ~2.9%+30¢ and must be looked up per provider, not assumed). |
| **Platform** | 20% | Covers infrastructure, AI costs, payment processing |

> **⚠️ The 20% is always computed on `Payment.Amount` in `Payment.Currency` — never converted to USD first.** A consultant charging in EGP has their commission computed in EGP; a consultant charging in SAR has it computed in SAR. Converting to USD before applying the percentage would make the split silently drift with exchange rate fluctuations, which is not what "20%" is supposed to mean. `Payment.Currency` is not a display detail — it's load-bearing for this calculation.

**Payout schedule — diverges by provider:**

*Stripe path (Saudi/UAE/etc.):*
- Consultant earnings accumulate in their Stripe Connect balance.
- Automatic weekly payout (Monday 00:00 UTC) to their linked bank account, in **their local payout currency** (Stripe converts automatically if the charge currency differs from the connected account's currency — this conversion is Stripe's, not something this platform calculates itself).
- Minimum payout threshold is defined **per currency**, not as a single USD figure (e.g. a flat $50 doesn't translate meaningfully to a SAR or AED threshold by simple conversion — set each threshold deliberately for that currency's market, not via FX math).

*Regional path (Egypt/etc.):*
- No automatic payout. Each paid appointment increments a `PayoutLedgerEntry` for that consultant (owed amount, in the original charge currency).
- An admin reviews and approves a batch payout (bank transfer initiated outside the system) via the Admin Dashboard (Section 3.3.2) — this makes the Admin Dashboard a **v1 dependency for the Egypt launch**, not a deferred v2 nice-to-have.
- Once a transfer is confirmed, the admin marks the relevant `PayoutLedgerEntry` rows as paid.

**Earnings calculation (provider-agnostic — works the same regardless of path):**
```
Total Earnings = SUM(Payments.Amount) WHERE Status = Paid AND RefundStatus != Succeeded
                 — computed per Payment.Currency, never aggregated across currencies into one number
Net Earnings   = Total Earnings × 0.80 − provider processing fees
Pending Payout = Net Earnings − Amount already paid out (Stripe transfer OR confirmed ledger settlement)
```

**Why 80/20?**
- v1 decision — industry standard for marketplace platforms. Applies identically regardless of provider/currency.
- Adjustable in v2 based on consultant tier (e.g., top-rated consultants get 85%) — a per-country or per-tier rate table is a v2 concern, not a v1 one.

**Refunds affect both parties:**
- When a refund is issued (cancellation >24h or consultant no-show), the **full amount** is refunded to the patient — via Stripe's refund API on the Stripe path, or via the regional gateway's refund mechanism (or, if unsupported by that gateway, a manual reversal — this depends on which regional provider is chosen and must be verified against Paymob/PayTabs's actual refund capabilities before implementation).
- The platform absorbs the loss (does not claw back from consultant for patient no-show; does not pay consultant for consultant no-show).
- On the regional path, a refunded appointment must **not** have created (or must reverse) a `PayoutLedgerEntry` — the ledger and the refund state must never disagree about whether a consultant is owed money for that appointment.
- `GET /consultants/me/earnings` excludes refunded appointments entirely, regardless of provider.

### 3.3.1a Stripe Connect Onboarding (Stripe path only)

> **Gap closed:** the Stripe path assumed a consultant's Stripe Connect account simply exists — nothing described how it gets created. `ConsultantProfile` gains `StripeConnectAccountId` and `StripeOnboardingComplete`. Flow:
> ```
> POST /consultants/me/stripe-onboarding
>   → creates a Stripe Connect Express account for this consultant (if none exists yet)
>   → returns an onboarding URL (Stripe-hosted KYC flow — identity, bank account)
> GET /consultants/me/stripe-onboarding-status
>   → checks current KYC completion via Stripe's Account API
>   → returns { complete: true/false, requirementsOutstanding: [...] }
> ```
> **Regional-path consultants never see these endpoints** — they have no Stripe Connect account at all; there's nothing to onboard.
> **Booking precondition:** `POST /appointments` must reject booking a Stripe-path consultant whose `StripeOnboardingComplete = false`, with `409 consultant-onboarding-incomplete` — an incomplete Connect account can't receive a Transfer.

### 3.3.1b Provider Fee Tracking — Actual, Not Estimated

> **Gap closed:** earlier drafts estimated Stripe's fee at "~2.9% + 30¢" for display in `GET /consultants/me/earnings`. That's an approximation, not what Stripe (or a regional gateway) actually charges for a given transaction — real fees vary by card type, currency, and country. `Payment` gains `ProviderFee` and `NetAmount`, populated from the provider's own reporting:
> - **Stripe path:** when handling `payment_intent.succeeded`, fetch the associated **Balance Transaction** (Stripe's API exposes the exact fee charged for that specific charge) and record it — not a formula-based guess.
> - **Regional path:** the regional gateway's webhook payload or settlement report supplies the equivalent figure; exact field depends on which gateway (Paymob/PayTabs) is chosen — verify against that provider's actual API before implementation.
> `GET /consultants/me/earnings`'s `providerFees` field (Section 3.3.1) now reflects `SUM(Payment.ProviderFee)` — real numbers, not an estimate formula.

### 3.3.1c Payment Failure Handling — Expiring Abandoned Bookings

> **Gap closed:** if a patient's `PaymentIntent` is created but they never complete payment (closes the app, abandons checkout), the `Appointment` stays `PendingPayment` indefinitely — silently holding the slot forever, since the filtered unique index (Section 3.2) only excludes `Cancelled`/`NoShow`/`PaymentFailed`, not `PendingPayment`.
> **`PendingPaymentExpiryBackgroundService`** (same polling-job pattern as the AI pipeline's stuck-job recovery, Section 3.5):
> ```
> Runs every 5 minutes
> Finds: Appointment.Status = PendingPayment AND CreatedAtUtc < Now.AddMinutes(-30)
> Action: Appointment.MarkPaymentFailed() → Status = PaymentFailed
> Effect: the filtered unique index now excludes this row, freeing the slot for other patients
> ```
> **30 minutes is a placeholder, not a validated figure** — pick a value that matches the actual patient checkout experience once that flow exists; the mechanism matters more than the exact number for v1.

### 3.3.2 Admin Dashboard — Elevated from v2 to v1.5

> **Decision:** given the Regional payout path above has no automated settlement, a minimal Admin Dashboard is required before the Egypt launch can function — this is no longer a deferred "nice to have," it's a dependency of Section 3.3.1. Scope is intentionally minimal (not a full admin suite):
> - View pending `PayoutLedgerEntry` rows, grouped by consultant.
> - Mark a batch as paid (after the admin performs the actual bank transfer outside the system).
> - Manual consultant verification (`ConsultantProfile.IsVerified`) — already a v1 requirement (Section 3.1) that had no UI; this gives it one instead of requiring direct DB edits.
> - Stack: **Angular** (separate project/repo from the Flutter apps and the ASP.NET Core API — talks to the same API Contract, no special admin-only backend).
> - Full analytics/reporting dashboards remain deferred to v2 (Architecture Document, Section 15) — this is strictly the minimum needed to make Section 3.3.1's regional path operable.

### 3.3.3 Stripe Connect Onboarding (Stripe path only)

A consultant routed to `IPaymentProvider.Stripe` (README §3.3.1) cannot receive payments until they complete Stripe's own KYC/bank-linking flow — this is separate from, and in addition to, the platform's own `ConsultantProfile.IsVerified` manual check (§3.1).

- As a Consultant (Stripe-path country), I want to start Stripe Connect onboarding from my profile, so that I can receive payouts.
- As a system, I want to block bookings for a Stripe-path consultant whose onboarding isn't complete, so that a payment is never taken for an appointment the platform can't actually pay out for.

**Flow:**
```
Consultant → POST /consultants/me/stripe-onboarding
        ↓
API creates a Stripe Connect Express account (if none exists yet)
        ↓
Returns a Stripe-hosted onboarding URL (KYC, bank account linking)
        ↓
Consultant completes onboarding on Stripe's site
        ↓
Stripe redirects back; API polls/webhooks to confirm completion
        ↓
ConsultantProfile.StripeOnboardingComplete = true
```

> **⚠️ Booking-time gate:** `POST /appointments` must check `StripeOnboardingComplete` for Stripe-path consultants before creating the appointment — a `409` (`consultant-onboarding-incomplete`) if incomplete. This has no equivalent check on the Regional path (Section 3.3.1), since that path never depends on a third-party KYC flow before it can collect payment.

### 3.3.4 Exact Provider Fee Tracking

The original earnings calculation *estimated* Stripe's fee (~2.9% + 30¢) at query time. This is imprecise — Stripe (and regional gateways) report the **actual** fee charged per transaction via their balance-transaction data, and fees can vary (currency, card type, region).

- As a system, I want to record the actual `ProviderFee` and resulting `NetAmount` on each `Payment` when the provider reports it (Stripe: from the balance transaction linked to the `PaymentIntent`; Regional: from whatever the chosen gateway's equivalent mechanism reports), so that `GET /consultants/me/earnings` reflects real numbers, not an estimate.
- This removes the need for `GET /consultants/me/earnings` to guess at a fee percentage — it simply sums the already-recorded `ProviderFee`/`NetAmount` columns.

> **⚠️ Timing:** the fee isn't known at the moment `Payment.Status` becomes `Paid` (the webhook confirms payment succeeded, but the balance-transaction fee detail is a separate Stripe API object). `RecordProviderFee` is a distinct step, called either from an expanded webhook handler (if Stripe includes the balance transaction in the event payload) or a short follow-up call to Stripe's API — this detail needs to be worked out at implementation time, not assumed.

### 3.3.5 Payment Failure Handling — Expiry Cleanup

`AppointmentStatus.PaymentFailed` already exists (Section 3.2), but nothing previously moved a stuck appointment into it. Without this, an appointment where the patient abandons payment mid-flow (closes the app, loses connection) stays `PendingPayment` **indefinitely**, holding the slot.

- As a system, I want a background job that expires stale `PendingPayment` appointments, so that abandoned bookings don't permanently block a slot.

```
PendingPaymentExpiryBackgroundService (runs every 5 minutes)
        ↓
Find: Appointment.Status = PendingPayment AND CreatedAtUtc < Now - 30 minutes
        ↓
Appointment.MarkPaymentFailed() → Status = PaymentFailed
        ↓
Filtered unique index (Section 3.2) excludes PaymentFailed — slot is now bookable again automatically
```

> **⚠️ Open parameter:** the 30-minute expiry window is a placeholder, not a finalized decision — tune based on actual observed abandonment behavior once real usage data exists.


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

- As a Patient, I want to describe my symptoms and get AI-suggested specialties **and matching verified consultants** (ranked by relevance), so that I can book the right doctor immediately without manual searching.
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

#### AI Doctor Recommendation (v1)

The symptom checker does not stop at suggesting a specialty. After the LLM returns the suggested specialty, the system queries the database for verified consultants matching that specialty and returns them ranked:

```
Patient describes symptoms
        ↓
AnalyzeSymptomsCommand → LLM
        ↓
Suggested Specialty (e.g., "Cardiology")
        ↓
Query: SELECT * FROM ConsultantProfiles
        WHERE Specialty = "Cardiology" AND IsVerified = true
        ORDER BY (experience_score) DESC
        LIMIT 5
        ↓
Return: { specialty, consultants: [ { id, name, bio, specialty } ] }
```

**Ranking factors (v1, simple):**
- `IsVerified = true` (hard filter — unverified doctors never appear)
- Specialty exact match (hard filter)
- Random shuffle within the matched set (v1 — no ratings yet since Reviews are v2)

**Ranking factors (v2, richer):**
- Average rating (Reviews table)
- Number of completed consultations
- Response time
- Patient feedback keywords

**API Response:**
```json
{
  "suggestedSpecialty": "Cardiology",
  "consultants": [
    { "consultantId": "...", "name": "Dr. Ahmed", "specialty": "Cardiology", "bio": "..." }
  ]
}
```

> **⚠️ No PHI in AI prompt:** The symptom description is sent to the LLM, but the patient's identity (name, email, health profile) is **never** included in the prompt. The LLM only sees: "Patient reports: [symptoms]". This protects privacy and reduces liability.

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

### 3.6 Notifications *(v1: SignalR + FCM Push)*
- As a Patient/Consultant, I want to receive in-app notifications for key events, so that I stay informed without checking manually.
- Events to cover in v1: Appointment booked, Appointment confirmed, Appointment cancelled, Appointment starting soon, Doctor joined consultation.
- As a system, I want to deliver in-app notifications via **SignalR** when the app is open, so that real-time updates are instant.
- As a system, I want to deliver **push notifications via FCM** when the app is in the background or closed, so that critical events (incoming call, appointment reminder, booking confirmation) are never missed.
- As a Patient/Consultant, I want to see which notifications are unread, so that I can tell what's new.

> **⚠️ Why FCM in v1, not v2:** In a Telehealth app, missing an incoming call because the app was closed is a critical failure — not a UX inconvenience. SignalR alone cannot wake a closed app. FCM is required for: (1) incoming call alerts (with CallKit/ConnectionService), (2) appointment reminders, (3) booking confirmations. This is a domain-driven decision, not premature optimization.

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
- **Rate Limiting:** AI endpoints are rate-limited per user to control cost and load (see 3.5). Auth endpoints have tiered rate limiting (10/min for login, 3/min for registration, 3/hour for forgot-password) to prevent brute-force and enumeration attacks.
- **Account Lockout:** After 5 consecutive failed login attempts, the account is locked for 15 minutes. Combined with generic error responses (no distinction between "wrong password" and "account locked"), this prevents credential stuffing and user enumeration.
- **Caching:**
  - Consultant profile listings can be cached briefly (~5 minutes) since they change infrequently.
  - Available slots must **not** be cached beyond a few seconds (10–30s max), since they change with every booking — stale cached slots would cause booking conflicts.
- **Auditability (lightweight in v1, full audit trail in v2):** Core entities carry `CreatedAt`/`ModifiedAt` and support soft deletes from v1, so a full `AuditLog` (EntityType, EntityId, Action, UserId, Timestamp, Changes) can be added later without restructuring existing tables.
- **Health Data Structure:** `PatientProfile.HealthProfile` is a structured Value Object (not free-text). All fields are optional (nullable) to respect patient privacy, but when provided they are validated (e.g., height 50–300cm, weight 2–500kg) and stored in a queryable format (JSON columns or owned entity).
- **Maintainability:** Backend follows Clean Architecture (Data/Application/Domain/Presentation) with CQRS/MediatR, consistent with existing project conventions.

---

## 6. Scope Split — MVP vs. Later

**MVP (v1):**
- Authentication (Patient/Consultant)
- Doctor Profiles (with `TimeZoneId`, `IsVerified`) / Patient Profiles
- Availability + Appointments (30-min slots, timezone-safe, concurrency-safe)
- Multi-provider payments + Payouts (Stripe Connect where supported; regional gateway + manual ledger settlement elsewhere — webhook-driven, idempotent, 80/20 commission split computed in the original charge currency, see Section 3.3.1)
- Chat (persisted + encrypted at rest, SignalR real-time delivery)
- SignalR + WebRTC (with STUN/TURN)
- AI Symptom Checker + **Doctor Recommendation** (via `IAIService` abstraction, structured output, rate-limited, returns verified consultants ranked by specialty match)
- AI Consultation Summary — chat-based, async pipeline with transactional job creation and DB polling, retry/failure handling and restart recovery
- In-app notifications (SignalR) + Push notifications (FCM) for critical events
- Native SDK Layer (Kotlin/Swift) for WebRTC media pipeline, secure storage, and background call handling
- **Payout schedule:** provider-dependent — automatic weekly (Stripe path) or manual ledger settlement via the minimal Admin Dashboard (regional path, Section 3.3.2)
- **Minimal Admin Dashboard (v1.5)** — payout ledger review/settlement + manual consultant verification (Angular, separate project)

**Later (v2+):**
- Full admin dashboard / analytics / user management beyond the v1.5 minimal scope
- Reviews & ratings
- Medical records / attachments
- Voice transcription for AI Summary (Speech-to-Text pipeline)
- Call recording
- Prescriptions
- Insurance integration
- Multi-language support
- Complex refund/cancellation policies
- Group video calls
- Full `AuditLog` table
- Redis SignalR backplane (only if scaling beyond one instance)
- Richer `RefreshToken` fields for multi-device session management (`DeviceId`, `UserAgent`, `IpAddress`, `RevokedReason`)
- App version gating / staged rollout strategy (Play Store staged rollout, App Store phased release, Shorebird tracks for Dart hotfixes) — deferred until a concrete operational need arises (e.g. a critical WebRTC bug requiring forced update for affected client versions)

---

## 7. Out of Scope (v1)

Explicitly *not* building initially:

- [ ] Multi-language support
- [ ] Admin dashboard / analytics
- [x] ~~Complex refund/cancellation policies~~ — **MOVED TO v1** (simple auto-refund >24h before appointment)
- [ ] Group video calls (one-to-one only)
- [ ] Insurance/claims integration
- [x] ~~Push notifications (FCM)~~ — **MOVED TO v1** (required for Telehealth: incoming calls, reminders, confirmations)
- [ ] Speech-to-Text / voice transcription for AI Summary — architecture supports it, but implementation deferred to v2 (chat-based summary only for v1)
- [ ] Full audit log table (basic `CreatedAt`/`ModifiedAt` + soft delete support only in v1)
- [ ] SignalR Redis backplane (single-instance only in v1)

---

## 7.5. Native SDK Layer — Flutter + Platform Channels

> **⚠️ This is a v1 addition, not a v2 deferral.** The decision to add a native SDK layer was made after the initial System Design series was complete, based on the specific needs of a Telehealth app (real-time media, secure storage, background call handling).

### Why native?

Flutter is excellent for UI and business logic, but a Telehealth app has three areas where native code provides critical advantages:

1. **WebRTC Media Pipeline** — `flutter_webrtc` abstracts the basics, but for production-grade video calls, direct control over camera capture, hardware encoding/decoding, and echo cancellation via native APIs (Camera2 on Android, AVFoundation on iOS) provides better performance and battery life.
2. **Secure Storage** — While `flutter_secure_storage` wraps Keychain/Keystore, a native SDK layer allows us to implement application-layer encryption for chat messages using platform-specific crypto APIs (Android Keystore System, iOS Secure Enclave) with hardware-backed keys where available.
3. **Background Call Handling** — Incoming calls must surface as system-level call UI (CallKit on iOS, ConnectionService/TelecomManager on Android). This is impossible from pure Flutter; it requires native services that bind to the OS telephony framework.

### Architecture — How it fits Clean Architecture

The native layer does **not** replace Flutter's Clean Architecture; it extends the Data Layer:

```
┌─────────────────────────────────────────────┐
│  Presentation Layer (Flutter UI + BLoC)     │
├─────────────────────────────────────────────┤
│  Domain Layer (UseCases, Entities)          │  ← 100% Dart, zero native deps
├─────────────────────────────────────────────┤
│  Data Layer                                   │
│  ├── API Repositories (Dio)                 │
│  ├── Local Repositories (Hive/Drift)        │
│  └── Native Repositories                    │  ← PlatformChannel → Kotlin/Swift
│      ├── WebRTCNativeService                │
│      ├── SecureStorageNativeService         │
│      └── CallKitConnectionService           │
└─────────────────────────────────────────────┘
```

**Rules:**
- Domain Layer defines interfaces (`IWebRTCNativeService`, `ISecureStorageService`).
- Data Layer implements them via `MethodChannel` / `EventChannel`.
- Native code (Kotlin/Swift) lives in `android/src/main/kotlin/...` and `ios/Runner/...` — standard Flutter plugin structure, but inline within the app (not a published plugin).
- No business logic in native code — native is a "dumb pipe" for platform capabilities. All decisions (when to start a call, what to encrypt) are made in Dart Domain Layer.

### Communication Pattern

```dart
// Dart side (Data Layer)
class WebRTCNativeService implements IWebRTCNativeService {
  static const platform = MethodChannel('com.telehealth.webrtc');

  @override
  Future<void> initializeLocalStream() async {
    await platform.invokeMethod('initializeLocalStream', {
      'videoEnabled': true,
      'audioEnabled': true,
      'facingMode': 'user',
    });
  }
}
```

```kotlin
// Android side (Kotlin)
class WebRTCNativePlugin : FlutterPlugin, MethodCallHandler {
    override fun onMethodCall(call: MethodCall, result: Result) {
        when (call.method) {
            "initializeLocalStream" -> {
                val videoEnabled = call.argument<Boolean>("videoEnabled") ?: true
                // Use Camera2 API + WebRTC native library
                nativeWebRTC.initialize(videoEnabled)
                result.success(null)
            }
        }
    }
}
```

```swift
// iOS side (Swift)
class WebRTCNativePlugin: NSObject, FlutterPlugin {
    static func register(with registrar: FlutterPluginRegistrar) {
        let channel = FlutterMethodChannel(name: "com.telehealth.webrtc", binaryMessenger: registrar.messenger())
        let instance = WebRTCNativePlugin()
        registrar.addMethodCallDelegate(instance, channel: channel)
    }

    func handle(_ call: FlutterMethodCall, result: @escaping FlutterResult) {
        switch call.method {
        case "initializeLocalStream":
            let args = call.arguments as! [String: Any]
            let videoEnabled = args["videoEnabled"] as? Bool ?? true
            // Use AVFoundation + WebRTC native framework
            nativeWebRTC.initialize(video: videoEnabled)
            result(nil)
        }
    }
}
```

### What goes native vs. what stays Flutter

| Concern | Flutter (Dart) | Native (Kotlin/Swift) |
|---|---|---|
| UI Screens / Navigation | ✅ | ❌ |
| State Management (BLoC) | ✅ | ❌ |
| API Calls (REST/SignalR) | ✅ | ❌ |
| Business Logic / Validation | ✅ | ❌ |
| Camera capture / Video encoding | ⚠️ (flutter_webrtc) | ✅ (direct Camera2/AVFoundation) |
| Hardware crypto (Secure Enclave/Keystore) | ❌ | ✅ |
| Background audio / CallKit | ❌ | ✅ |
| Picture-in-Picture video | ⚠️ | ✅ |
| File I/O / Local DB | ✅ | ❌ |

### v1 Scope for Native Layer

**In scope (v1):**
- `SecureStorageService` — hardware-backed key storage for chat encryption keys + refresh token secure storage
- `WebRTCNativeService` — camera/audio initialization, hardware codec selection, echo cancellation tuning
- `CallKitService` (iOS) / `ConnectionService` (Android) — incoming call UI integration with OS

**Deferred to v2:**
- Native video recording / screenshot prevention (DRM-like)
- Advanced noise suppression (RNNoise native integration)
- Native haptics for call notifications

---

## 8. Decisions Made

- **AI Provider:** `Microsoft.Extensions.AI` as the abstraction layer, with **OpenAI** as the concrete provider behind it in v1. This teaches the abstraction pattern while still exercising a real provider integration.
- **Cancellation Policy (v1):** Simple fixed rule — free cancellation up to 24h before the appointment with **automatic full refund**; cancellations within 24h of the appointment time are not refunded (the payment is kept). This applies equally whether the patient or consultant initiates the cancellation. Refund is processed via Stripe and confirmed by webhook.
- **Pricing Model (v1):** Flat consultation price across all consultants (no per-specialty or per-consultant variable pricing yet).
- **STUN/TURN Provider:** Google's public STUN server for v1 (free, sufficient for learning NAT traversal). Add a self-hosted **coturn** TURN server as a follow-up step to also learn the relay/infrastructure side.
- **Background Job Execution (v1):** No message queue or Hangfire yet — start with the simplest reliable pattern (Hosted BackgroundService + persisted `AIJob` state, see 3.5). Once understood end-to-end, optionally build a second version using Hangfire or a message broker to compare trade-offs — a deliberate v2 learning exercise, not a v1 requirement.
- **Consultant Verification (v1):** `IsVerified` boolean, manually flipped in the database for now — no admin UI needed until v2.
- **AI Summary cardinality — one final summary per consultation in v1:** `Consultation → 0..1 AISummary` is a deliberate decision, not just a default cardinality. `AIJob` already models retries as a single row with an incrementing `RetryCount` (not one row per attempt), so only the successful attempt ever persists an `AISummary`. If a future version needs to keep a history of summaries across different prompt versions or providers (true versioning, not retry), this relationship would need to change to `Consultation → 0..* AISummary` with an `IsCurrent`/`GeneratedAt` marker to identify the latest — that's an explicit v2 schema change, not something v1 needs to accommodate now.
- **RefreshToken fields (v1 minimal set):** `UserId`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt` — sufficient for v1. Richer fields (`DeviceId`, `UserAgent`, `IpAddress`, `RevokedReason`) are useful for multi-device session management and audit trails, but are a deliberate v2 addition, not required to ship v1 securely.
- **Admin Dashboard Stack (v2):** ✅ Angular (not Flutter Web). Admin dashboards are web-only, data-heavy, and benefit from Angular's reactive forms, routing, and rich component ecosystem. The user is already learning Angular, making this a natural fit. Flutter Web is not suitable for complex admin UIs with data tables, charts, and complex forms.
- **Native SDK Layer:** ✅ Added as v1 component. Flutter handles UI + business logic; Kotlin (Android) and Swift (iOS) handle platform-specific capabilities (WebRTC media pipeline, hardware crypto, CallKit/ConnectionService) via Platform Channels. Domain Layer remains 100% Dart with interface abstractions.
- **Push Notifications (FCM):** ✅ Moved to v1. SignalR handles in-app real-time; FCM handles background/closed-app delivery. Both channels are triggered from the same `INotificationService` abstraction. Device tokens are stored per-user (`UserDeviceTokens` table). FCM is not deferred to v2 because a Telehealth app without background call alerts is not viable.
- **Consultant TimeZoneId at Registration:** ✅ Finalized — `POST /auth/register` does **not** include `timeZoneId` (role-agnostic endpoint). `ConsultantProfile` is created with `TimeZoneId = null`. The consultant must set it via `PUT /consultants/me` before setting availability (`PUT /consultants/me/availability` returns `409` if null). This keeps registration simple while enforcing that availability is never computed without a timezone.
- **Patient Health Data Model:** ✅ Finalized — `HealthProfile` is a structured Value Object (not a `string`). Contains: `HeightCm`, `WeightKg`, `BloodType` (enum), `SmokingStatus` (enum), `Allergies`/`ChronicConditions`/`CurrentMedications` (JSON lists). Stored as an owned entity / JSON column in SQL Server. This enables validation, querying, structured AI context, and clean UI rendering. A `string` field was rejected as an anti-pattern for medical data.
- **Refund Flow (v1):** ✅ Finalized — automatic full refund for cancellations >24h before appointment via Stripe API + webhook confirmation. `Payment.RefundStatus` tracks lifecycle. Cancellations <24h are not refunded. Earnings endpoint excludes refunded payments.
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
- Users *(+ `EmailConfirmed`, `EmailVerificationTokenHash`, `EmailVerificationSentAt`)*
- ConsultantProfiles *(+ `TimeZoneId`, `IsVerified`, `ProfileImageUrl`, `Country` — required at profile completion; determines which `IPaymentProvider` routes this consultant's payments, see Section 3.3.1; + `StripeConnectAccountId`, `StripeOnboardingComplete` — Stripe path only, see Section 3.3.3)*
- PatientProfiles *(+ `HealthProfile` value object: `HeightCm`, `WeightKg`, `BloodType`, `SmokingStatus`, `Allergies` [JSON], `ChronicConditions` [JSON], `CurrentMedications` [JSON]) — replaces v17 `HealthInfo` string*
- AvailabilitySlots
- Appointments *(+ `CancellationReason`, `CancelledAt`, `IdempotencyKey` UNIQUE — see booking idempotency note in Section 3.2)*
- Consultations
- Payments *(+ `IdempotencyKey`, `Provider` [Stripe/Regional] — determines which payout path applies, `ProviderFee`, `NetAmount` — actual fee reported by the provider's balance-transaction data, not an estimated percentage, see Section 3.3.4, `StripeRefundId`, `RefundStatus` [None/Pending/Succeeded/Failed], `RefundedAt`; `StripeClientSecret` deliberately NOT persisted — see Section 3.3)*
- ChatMessages *(+ `MessageType`, encrypted `Content`)*
- AISummaries *(`ConsultationId` UNIQUE — backstops the `0..1` cardinality against a retry-after-partial-failure race)*
- AIJobs *(status, retries, provider, prompt version, errors, + `ProcessingStartedAt`, `ErrorDetails`)*
- Notifications *(+ `IsRead`, `ReadAt`, `Data` JSON for deep links)*
- RefreshTokens *(store `TokenHash`, never the raw token — see security note below; + `FamilyId`, `ReplacedByTokenId`, `RevocationReason`, `RequestIpAddress`, `UserAgent` for rotation & reuse detection)*
- StripeWebhookEvents *(`StripeEventId` UNIQUE — makes webhook idempotency concrete, see Section 3.3)*
- PasswordResetTokens *(+ `TokenHash` UK, `IsUsed`, `UsedAt`, `RequestIpAddress`, `UserAgent` — same hash-only pattern as RefreshTokens)*
- FailedLoginAttempts *(audit-only table for forensic analysis; lockout logic handled by ASP.NET Core Identity)*
- UserDeviceTokens *(FCM push tokens — `Token` UK, `Platform`, `LastUsedAt`; see Section 3.6)*
- PayoutLedgerEntries *(new, v1.5 — tracks amounts owed to consultants on the Regional payment path, since there's no automatic Stripe-style transfer to reconcile against; fields: `ConsultantId`, `PaymentId`, `AmountOwed`, `Currency`, `Status` [Pending/Paid/Refunded — `Refunded` set if the underlying Payment is refunded before settlement, keeping the row as an audit record rather than deleting it], `SettledAt`, `SettledByAdminUserId` — see Section 3.3.1/3.3.2)*

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

## 11a. Load Testing (K6)

> Added because the concurrency guarantees this whole document relies on (Section 3.2's filtered unique indexes, the webhook atomic-INSERT dedup, the AIJob atomic claim) are **design-time claims until something actually tries to break them under load**. A test suite that only exercises the happy path at low volume never proves any of that.

```
k6-tests/
├── scenarios/
│   ├── booking-flow.js         — full happy-path: search → book → pay
│   ├── concurrent-booking.js   — the important one: N patients hitting
│   │                              POST /appointments for the SAME slot
│   │                              simultaneously; expects exactly 1
│   │                              success and N-1 clean 409s, never 2 bookings
│   └── webhook-idempotency.js  — fires the same Stripe webhook payload
│                                  twice in parallel; expects exactly 1
│                                  Payment.Status=Paid transition, not 2
├── config/
│   └── thresholds.js           — p95 < 500ms, error rate < 1%
└── README.md
```

**`concurrent-booking.js` is the load test that actually matters most** — it's the only way to verify the filtered unique index (Section 3.2) holds under real concurrent writes rather than just being correct on paper. `webhook-idempotency.js` does the equivalent for the atomic-INSERT dedup pattern (Sequence Diagrams, Diagram 1). Both are regression tests for specific, previously-identified race conditions in this document — not generic load tests.

---

## 12. Next Steps

**Completed (System Design series):**
1. ✅ ERD — finalized (entities, relationships, filtered unique indexes, `AppointmentStatus` vs. `Payment.Status` resolved)
2. ✅ Architecture Diagram + Deployment Diagram — finalized
3. ✅ Sequence Diagrams — finalized for the three complex flows (Booking → Payment → Confirmation, WebRTC call establishment, Consultation → AI Summary)
4. ✅ API Contract — finalized (endpoint list, request/response shapes, validation, authorization, status codes, SignalR Hub contracts)
5. ✅ Security & Authentication Deep-Dive — finalized (Forgot/Reset Password, Email Verification, Change Password, Password Policy, Refresh Token Security with Rotation & Reuse Detection, Account Lockout / Brute-force Protection)

**Remaining before implementation:**
6. Design the **`IAIService` interface contract** — `AnalyzeSymptomsAsync` and `SummarizeConsultationAsync` signatures, input/output DTOs, and provider-agnostic abstractions. This is an internal implementation contract, not part of the public API surface.
7. Decide the **chat encryption approach** (application-layer vs. SQL Server column-level encryption) before implementing `ChatMessage`.
8. **Implementation** — begin coding against the finalized contracts above.
