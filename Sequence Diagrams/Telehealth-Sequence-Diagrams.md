# AI Telehealth Platform — Sequence Diagrams

**System Design Series · Document 3 of 4** (ERD → Architecture → **Sequence Diagrams** → API Contract)
**Stack:** ASP.NET Core 10 (Clean Architecture, CQRS/MediatR) + Flutter (Clean Architecture, BLoC)
**Status:** v8 — aligned with Requirements Document v19. Added No-Show Detection sequence diagram (background job, fault attribution, late-join protection).. Added Cancellation + Refund Flow sequence diagram.. Added Native SDK Layer sequence diagram (Platform Channel communication for WebRTC + Secure Storage). and Security Deep-Dive Document 5. Added Auth/Security sequence diagrams (Forgot/Reset Password, Email Verification, Refresh Token Rotation & Reuse Detection).

---

## Why only three diagrams

A Sequence Diagram isn't needed for every endpoint — only for flows with real decisions, branching, or timing complexity. The flows below are the ones with actual complexity worth working out **before** writing code:

1. **Booking → Payment → Confirmation** — transaction boundaries, concurrency, idempotency
2. **WebRTC Call Establishment** — signaling vs. media, STUN/TURN fallback
3. **Consultation → AI Summary** — async processing, retry, failure handling
4. **Forgot / Reset Password** — anti-enumeration, token security, session invalidation
5. **Email Verification** — registration flow, verification gate
6. **Refresh Token Rotation & Reuse Detection** — rotation, family binding, theft detection
7. **Flutter → Native SDK Communication** — Platform Channel pattern for WebRTC media pipeline and hardware-backed encryption

---

## 1. Booking → Payment → Confirmation

```mermaid
sequenceDiagram
    participant P as Patient (Flutter)
    participant API as ASP.NET Core API
    participant DB as SQL Server
    participant ST as Stripe

    P->>API: POST /appointments (Idempotency-Key: client GUID)
    API->>DB: Existing Appointment with this key?
    alt Key exists AND PaymentIntent already created
        DB-->>API: Existing result (final)
        API-->>P: 201 (existing appointmentId, clientSecret)
    else Key exists BUT no PaymentIntent yet (previous attempt died before Stripe)
        DB-->>API: Existing Appointment (PendingPayment, no PaymentIntent)
        Note right of API: Resume — do NOT just return early.<br/>Fall through to PaymentIntent creation below.
    else New key
        API->>API: Authenticate (JWT) + Authorize
        API->>DB: Check availability + overlaps
        Note right of DB: Filtered unique indexes:<br/>(ConsultantId, ScheduledStartUtc)<br/>(PatientId, ScheduledStartUtc)<br/>WHERE Status NOT IN (Cancelled, NoShow, PaymentFailed)
        API->>DB: Create Appointment (PendingPayment, IdempotencyKey)
    end
    API->>ST: Create PaymentIntent (Idempotency-Key = Appointment.IdempotencyKey)
    alt PaymentIntent creation fails
        ST-->>API: Error (network / Stripe downtime)
        API-->>P: 502 Payment initialization failed
        Note right of DB: Appointment stays PendingPayment.<br/>Retry with the SAME key resumes here again.
    else PaymentIntent created
        ST-->>API: PaymentIntent + ClientSecret
        API->>DB: Store PaymentIntentId
        API-->>P: 201 Created (appointmentId, clientSecret — not persisted)
    end
    P->>ST: Confirm payment (Stripe SDK, client-side)
    Note over P,ST: Backend never trusts this step
    ST->>API: Webhook: payment_intent.succeeded
    API->>API: Verify signature
    API->>DB: BEGIN TRANSACTION
    API->>DB: INSERT StripeWebhookEvent (StripeEventId)
    alt Insert fails (unique violation — already claimed)
        DB-->>API: Duplicate
        API->>DB: ROLLBACK
        API-->>ST: 200 OK (no reprocessing)
    else Insert succeeds — this request owns the event
        DB-->>API: Claimed
        API->>DB: Payment.Status = Paid
        API->>DB: Appointment.Status = Confirmed
        API->>DB: Create Notification
        Note right of DB: One transaction — never leaves<br/>Payment=Paid with Appointment still<br/>PendingPayment, or Confirmed with no Notification.<br/>INSERT StripeWebhookEvent is inside the same TX.<br/>If any step fails, the whole webhook rolls back<br/>and Stripe's retry will redeliver.
        API->>DB: COMMIT
        API-->>P: SignalR: appointment confirmed
    end
```

### What this flow locks in before implementation

- **"Key already exists" is not automatically "return it and stop" — this was a real bug in an earlier draft.** The retry behavior has to depend on how far the *previous* attempt actually got:
  - `PaymentIntent` already created → the original attempt succeeded past that point; return the existing result, nothing to redo.
  - `PendingPayment` with **no** `PaymentIntent` yet → the original attempt died *before* reaching Stripe. The correct behavior is to **resume** — fall through and attempt `PaymentIntent` creation now — not to return a half-finished result and strand the booking. This is exactly the scenario the Idempotency-Key was added to protect against; returning early here would have silently defeated its own purpose.
- **Webhook deduplication is atomic by construction, not by a check that races against itself.** A `SELECT ... WHERE StripeEventId = ?` followed by a separate insert has a race window: two near-simultaneous deliveries of the same event could both pass the check before either has recorded it. Instead, **the `INSERT` itself is the atomicity boundary** — attempt to insert the `StripeEventId` row first; if it fails on the `UNIQUE` constraint, another request already claimed this event and this one returns `200` without reprocessing; if it succeeds, this request has exclusively claimed the event.
- **The whole webhook effect is one transaction**, not four separate writes: `INSERT StripeWebhookEvent` + `Payment.Status = Paid` + `Appointment.Status = Confirmed` + `Create Notification`, committed together. This closes off partial-failure states that a sequence of independent writes could otherwise leave behind — e.g. `Payment = Paid` but `Appointment` still `PendingPayment`, or a `Confirmed` appointment with no `Notification` ever created. `SignalR` delivery happens only *after* the transaction commits.
- **Webhook order still matters: verify signature *first*, then touch `StripeEventId`.** The event ID is untrusted input until the signature proves the payload came from Stripe.
- **Two idempotency keys, two different failure points:** the booking-level `Idempotency-Key` (protects a lost response from `POST /appointments`) and the Stripe-level key (protects the `PaymentIntent` call) are the *same* value (`Payment.IdempotencyKey = Appointment.IdempotencyKey`), reused end-to-end rather than generated separately.
- **`StripeClientSecret` is never persisted** — returned to Flutter at creation time only, consistent with the Requirements Document decision (no v1 use case needs the backend to retrieve it later).
- **Retrieval on retry is required and must be documented in the API Contract.** Because `StripeClientSecret` is not stored, the retry path with an existing `PaymentIntentId` must retrieve the `PaymentIntent` from Stripe (`GET /v1/payment_intents/{id}`) to extract the `ClientSecret` before returning it to Flutter. This is a single, safe call — the PaymentIntent was created by this same backend.
- **Field naming is `ScheduledStartUtc` everywhere**, and the booking constraints are *filtered* unique indexes (`WHERE Status NOT IN (Cancelled, NoShow, PaymentFailed)`) so a cancelled appointment never permanently blocks a slot from being rebooked.

---

## 2. WebRTC Call Establishment

```mermaid
sequenceDiagram
    participant P as Patient
    participant Hub as SignalR Hub
    participant API as ASP.NET Core API
    participant C as Consultant

    P->>Hub: JoinCall(appointmentId)
    Hub->>API: Authorize + validate
    Note right of API: 1. Caller is a participant (resource ownership)<br/>2. Appointment.Status in [Confirmed, InProgress]
    alt Not a participant, or wrong status
        API-->>Hub: Rejected
        Hub-->>P: Cannot join call
    else Allowed
        API-->>Hub: Authorized
        Hub->>C: Notify: incoming call
        C->>Hub: JoinCall(appointmentId)

        P->>Hub: SDP Offer
        Hub->>C: Relay SDP Offer
        C->>Hub: SDP Answer
        Hub->>P: Relay SDP Answer
        P->>Hub: ICE Candidates
        Hub->>C: Relay ICE Candidates

        Note over P,C: Signaling only — Hub never carries media

        alt Direct P2P succeeds (STUN)
            P->>C: Media (audio/video) — direct
        else P2P fails (restrictive NAT/firewall)
            P->>C: Media relayed via TURN
        end
    end
```

### What this flow locks in before implementation

- **Authorization is two checks, not one:** identity (is this caller a participant?) and state (is this appointment currently joinable?) — a real participant could still try to join a `Cancelled` or already-`Completed` appointment.
- **The Hub's job ends at signaling.** Once the peer connection is established, the Hub is no longer in the path — media never touches it.
- **STUN vs. TURN is a runtime fallback**, always attempting direct P2P first and relaying through TURN only when that fails.

---

## 3. Consultation → AI Summary

```mermaid
sequenceDiagram
    participant C as Consultant
    participant API as ASP.NET Core API
    participant DB as SQL Server
    participant BG as BackgroundService
    participant AI as IAIService → OpenAI

    C->>API: POST /consultations/{id}/complete
    API->>DB: TRANSACTION: Consultation=Completed + AIJob=Pending
    Note right of DB: Same DB transaction — not a<br/>published event (no Outbox in v1)
    API-->>C: 200 OK (immediate, no waiting on AI)

    loop Poll for work
        BG->>DB: Atomic claim: Pending → Processing (1 row)
        Note right of DB: Conditional UPDATE — safe even<br/>if BackgroundService scales to 2+ instances
        BG->>DB: Collect chat messages (v1 context)
        BG->>AI: SummarizeConsultationAsync(context)
        alt Success
            AI-->>BG: Structured output (JSON)
            BG->>BG: Validate structured output
            BG->>DB: TRANSACTION: Insert AISummary + AIJob=Completed
            Note right of DB: Same transaction — a crash between<br/>the two writes can't leave AIJob<br/>stuck Processing with no summary saved
            BG->>API: Notify Consultant
            API-->>C: SignalR: summary ready
        else Failure (timeout / rate limit / invalid)
            alt RetryCount < MaxRetries
                BG->>DB: RetryCount++, Status = Pending (backoff delay)
            else RetryCount reached MaxRetries
                BG->>DB: Status = Failed (terminal)
            end
        end
    end
```

### What this flow locks in before implementation

- **`AIJob` is created transactionally**, in the same DB transaction as `CompleteConsultation` — not via a published event, which would only be safe with an Outbox Pattern (out of scope for v1).
- **Persisting the summary and completing the job are one transaction**, closing the window where a crash between the two writes could leave `AIJob` stuck `Processing` despite the summary already being saved.
- **Job claiming is atomic** — a single conditional update, not read-then-write — safe even if `BackgroundService` were ever scaled beyond one instance.
- **Context is explicitly v1-scoped:** chat messages only; voice transcription is a v2 addition.
- **Retry is bounded**, with a `MaxRetries` ceiling before moving to a terminal `Failed` status.
- **`AISummaries.ConsultationId` is unique**, enforcing the `0..1` cardinality at the database level.

---

## Cross-cutting pattern: persist first, notify second

All three flows share the same shape at the end: **write to the database (as one transaction where multiple writes are involved), then notify** — never the reverse.

```
Booking:      [Payment=Paid + Appointment=Confirmed + Notification] (1 TX) → SignalR
AI Summary:   [AISummary + AIJob=Completed] (1 TX) → SignalR
```

`SignalR` is a **delivery mechanism**, not a source of truth — if the recipient is offline, the notification simply stays unread in the `Notifications` table rather than being lost. And because the underlying state changes are wrapped in single transactions rather than sequences of independent writes, there's no partial-state window for a crash to expose in the first place.

---

**Next in the System Design series:** the full API Contract — endpoint list, request/response shapes, validation, authorization, and status codes, building directly on the flows above.


---

## 4. Forgot / Reset Password

### 4.1 Forgot Password — Anti-Enumeration Flow

```mermaid
sequenceDiagram
    participant F as Flutter
    participant API as ASP.NET Core API
    participant DB as SQL Server
    participant MQ as Email Service (SendGrid/AWS SES)

    F->>API: POST /auth/forgot-password { email }
    API->>DB: SELECT User WHERE Email = ?
    alt User exists AND EmailConfirmed = true
        API->>DB: DELETE old unused tokens for this user
        API->>API: Generate crypto-random token (256-bit)
        API->>API: Hash token (SHA256)
        API->>DB: INSERT PasswordResetToken (TokenHash, ExpiresAt = Now+1h)
        API->>MQ: Send reset email (raw token in URL)
        Note right of API: URL: /reset-password?token=RAW_TOKEN&email=user@example.com
    else User does NOT exist OR EmailConfirmed = false
        Note right of API: Do NOTHING — no DB write, no email
    end
    API-->>F: 200 OK { "message": "If this email exists..." }
    Note right of F: نفس الرد تمامًا في الحالتين — لا يوجد طريقة للتمييز
```

**Key decisions locked:**
- **Same response regardless of existence** — prevents user enumeration.
- **Timing normalization** — the "not found" path may include a small random delay (50–150ms) so both paths take approximately the same time, preventing timing attacks.
- **No email to unverified addresses** — prevents email bombing of arbitrary addresses.
- **Token hashing** — only `SHA256(Token)` is persisted; raw token exists only in the user's email inbox.

### 4.2 Reset Password — Session Kill

```mermaid
sequenceDiagram
    participant F as Flutter
    participant API as ASP.NET Core API
    participant DB as SQL Server

    F->>API: POST /auth/reset-password { token, email, newPassword }
    API->>API: Hash incoming token (SHA256)
    API->>DB: SELECT token WHERE TokenHash = ? AND User.Email = ?
    alt Token not found OR expired OR already used
        API-->>F: 400 Bad Request { "title": "Invalid or expired token" }
    else Token valid
        API->>API: Validate newPassword against policy
        API->>DB: BEGIN TRANSACTION
        API->>DB: UPDATE User.PasswordHash (ASP.NET Core Identity)
        API->>DB: UPDATE PasswordResetToken SET IsUsed = true, UsedAt = Now
        API->>DB: DELETE ALL RefreshTokens for this user
        API->>DB: COMMIT
        API-->>F: 200 OK { "message": "Password reset successful. Please log in." }
    end
```

**Key decisions locked:**
- **Single-use token** — `IsUsed` flag makes the token permanently invalid after first use.
- **Post-reset nuclear option** — all `RefreshTokens` for the user are deleted, forcing re-login on all devices. This is a security-first decision: if the user forgot their password, we assume they may have been compromised.
- **Transaction boundary** — password update + token consumption + session invalidation happen in one transaction. A crash can't leave the password updated but sessions still valid.

---

## 5. Email Verification

```mermaid
sequenceDiagram
    participant F as Flutter
    participant API as ASP.NET Core API
    participant DB as SQL Server
    participant MQ as Email Service

    F->>API: POST /auth/register { email, password, role }
    API->>API: Validate password policy
    API->>API: Generate verification token (256-bit)
    API->>API: Hash token (SHA256)
    API->>DB: TRANSACTION:
    Note right of DB: 1. INSERT User (EmailConfirmed = false)
    Note right of DB: 2. INSERT Profile (Patient/Consultant)
    Note right of DB: 3. Store EmailVerificationTokenHash
    API->>DB: COMMIT
    API->>MQ: Send verification email (raw token in link)
    API-->>F: 201 Created { userId, emailConfirmed: false }
    Note right of F: Flutter shows "Please verify your email" screen

    F->>API: POST /auth/verify-email { email, token }
    API->>API: Hash incoming token
    API->>DB: SELECT User WHERE Email = ? AND TokenHash = ?
    alt Token valid and not expired (24h)
        API->>DB: UPDATE User SET EmailConfirmed = true, TokenHash = NULL
        API-->>F: 200 OK { emailConfirmed: true }
    else Token invalid/expired
        API-->>F: 400 Bad Request
    end
```

**Key decisions locked:**
- **Verification gate** — `EmailConfirmed = false` blocks booking (`POST /appointments` returns `403`) and payment (implicitly, via booking block).
- **Token storage** — `EmailVerificationTokenHash` on `Users` table (v1 minimal). v2 may split to a separate `EmailVerificationTokens` table for audit trail.
- **Auto-send on registration** — no separate "send verification" step required; the email goes out automatically as part of the registration transaction.

---

## 6. Refresh Token Rotation & Reuse Detection

### 6.1 Normal Rotation

```mermaid
sequenceDiagram
    participant F as Flutter
    participant API as ASP.NET Core API
    participant DB as SQL Server

    F->>API: POST /auth/refresh { refreshToken }
    API->>API: Hash incoming token (SHA256)
    API->>DB: SELECT token WHERE TokenHash = ? AND RevokedAt IS NULL AND ExpiresAt > Now
    alt Token not found OR expired OR revoked
        API-->>F: 401 Unauthorized (generic)
    else Token valid
        API->>API: Generate new crypto-random token
        API->>API: Hash new token (SHA256)
        API->>DB: BEGIN TRANSACTION
        API->>DB: INSERT new RefreshToken (same FamilyId)
        API->>DB: UPDATE old token: RevokedAt = Now, ReplacedByTokenId = newId, Reason = ReplacedByRotation
        API->>DB: COMMIT
        API->>API: Issue new JWT access token
        API-->>F: 200 OK { accessToken, refreshToken: NEW_RAW, expiresIn: 3600 }
    end
```

### 6.2 Reuse Detection — Theft Scenario

```mermaid
sequenceDiagram
    participant Attacker as Attacker (stole old token)
    participant Legit as Legitimate User
    participant API as ASP.NET Core API
    participant DB as SQL Server

    Note over Attacker,Legit: Attacker tries to use a token that was already rotated
    Attacker->>API: POST /auth/refresh { stolenOldToken }
    API->>API: Hash token
    API->>DB: SELECT token
    API->>DB: Token found BUT RevokedAt IS NOT NULL (already rotated!)
    API->>DB: BEGIN TRANSACTION
    API->>DB: UPDATE token SET RevocationReason = ReuseDetected
    API->>DB: DELETE ALL RefreshTokens WHERE FamilyId = thisFamily
    API->>DB: COMMIT
    API-->>Attacker: 401 Unauthorized (generic)

    Note over Legit: Legitimate user tries to use their latest valid token
    Legit->>API: POST /auth/refresh { hisValidToken }
    API->>DB: Token found BUT family was wiped (ReuseDetected)
    API-->>Legit: 401 Unauthorized (generic)
    Note right of Legit: User forced to re-login — attacker is also blocked
```

**Key decisions locked:**
- **Family binding** — all tokens from the same initial login share a `FamilyId`. This enables the "nuclear option": one detected reuse invalidates the entire family.
- **Generic 401** — regardless of failure reason (expired, revoked, reuse detected), the response is identical. No information leakage to attackers.
- **Token lifetime** — Refresh: 7 days. Access (JWT): 15 minutes.
- **Cleanup** — Hosted BackgroundService deletes expired tokens (>30 days old) to prevent table bloat.

---

## Cross-cutting pattern: persist first, notify second (extended)

All flows share the same shape at the end: **write to the database (as one transaction where multiple writes are involved), then notify** — never the reverse.

```
Booking:      [Payment=Paid + Appointment=Confirmed + Notification] (1 TX) → SignalR
AI Summary:   [AISummary + AIJob=Completed] (1 TX) → SignalR
Reset Password: [PasswordHash updated + Token marked used + RefreshTokens deleted] (1 TX) → HTTP 200
Change Password: [PasswordHash updated + RefreshTokens deleted] (1 TX) → HTTP 200
```

`SignalR` / HTTP response is a **delivery mechanism**, not a source of truth — if the recipient is offline or the request fails, the underlying state change is still safely committed.


---

## 7. Flutter → Native SDK Communication (Platform Channels)

This diagram shows how the Flutter app delegates platform-specific capabilities to native Kotlin (Android) and Swift (iOS) code while maintaining Clean Architecture boundaries.

### 7.1 WebRTC Media Initialization

```mermaid
sequenceDiagram
    participant F as Flutter (Dart)
    participant B as BLoC / UseCase
    participant D as Domain Interface (Dart)
    participant R as NativeRepository (Dart)
    participant CH as MethodChannel
    participant NA as Native Plugin (Kotlin/Swift)
    participant OS as OS APIs (Camera2/AVFoundation)

    F->>B: User taps "Join Call"
    B->>D: IWebRTCNativeService.initializeLocalStream()
    D->>R: WebRTCNativeServiceImpl
    R->>CH: invokeMethod('initializeLocalStream', args)
    CH->>NA: onMethodCall received
    NA->>OS: Camera2 / AVFoundation setup
    OS-->>NA: Local media stream ready
    NA-->>CH: result.success(streamId)
    CH-->>R: Future completes
    R-->>D: Stream initialized
    D-->>B: Success
    B-->>F: Update UI (show local preview)
```

### 7.2 Secure Storage — Hardware-Backed Key Generation

```mermaid
sequenceDiagram
    participant F as Flutter (Dart)
    participant B as BLoC / UseCase
    participant D as Domain Interface (Dart)
    participant R as NativeRepository (Dart)
    participant CH as MethodChannel
    participant NA as Native Plugin (Kotlin/Swift)
    participant OS as Keystore / Secure Enclave

    F->>B: Chat message to send
    B->>D: ISecureStorageNativeService.getOrCreateEncryptionKey()
    D->>R: SecureStorageNativeServiceImpl
    R->>CH: invokeMethod('getOrCreateKey', {keyAlias: 'chat_encryption'})
    CH->>NA: onMethodCall received
    alt Key exists in Keystore/Secure Enclave
        NA->>OS: Retrieve existing key
        OS-->>NA: Key reference (never leaves hardware)
    else Key does not exist
        NA->>OS: Generate new AES-256-GCM key (hardware-backed)
        OS-->>NA: Key created
    end
    NA->>NA: Encrypt plaintext using hardware key
    NA-->>CH: result.success(encryptedData)
    CH-->>R: Encrypted payload
    R-->>D: Encrypted data ready
    D-->>B: Return encrypted content
    B->>B: Send encrypted content via SignalR
```

### 7.3 Incoming Call — CallKit (iOS) / ConnectionService (Android)

```mermaid
sequenceDiagram
    participant API as ASP.NET Core API
    participant PN as Push Notification (FCM/APNs)
    participant NA as Native Plugin (Kotlin/Swift)
    participant OS as CallKit / ConnectionService
    participant CH as EventChannel
    participant F as Flutter (Dart)
    participant B as BLoC

    API->>PN: Send push notification (v2)
    PN->>NA: Notification received (background)
    NA->>OS: Report incoming call
    OS->>OS: Show system call UI
    OS-->>NA: User answered
    NA->>CH: EventChannel.send({event: 'call_answered'})
    CH->>F: Stream listener triggered
    F->>B: IncomingCallAnswered event
    B->>B: Navigate to call screen
    B->>B: JoinCall via SignalR
```

> **⚠️ Note:** Push notifications (FCM/APNs) are v2, but the native plugin structure for CallKit/ConnectionService must be designed in v1 so the architecture supports it. In v1, the incoming call is triggered by SignalR while the app is foreground, but the native service registration happens at app startup.

### What these diagrams lock in

- **Native code is a capability provider, not a decision maker.** All business logic (when to start a call, what to encrypt) stays in Dart Domain Layer.
- **MethodChannel for request/response** (synchronous-like calls: initialize camera, encrypt data).
- **EventChannel for streaming events** (asynchronous: incoming call events, ICE candidate events from native WebRTC).
- **Hardware-backed encryption keys never leave the secure hardware.** The native plugin encrypts/decrypts on the native side; only ciphertext crosses the Platform Channel boundary.
- **Clean Architecture preserved:** Domain Layer defines interfaces; Data Layer implements them using Platform Channels.


---

## 8. Cancellation + Refund Flow

This diagram covers both patient-initiated and consultant-initiated cancellations. The refund rule is identical for both: full automatic refund if cancelled more than 24 hours before the scheduled time; no refund if within 24 hours.

```mermaid
sequenceDiagram
    participant U as User (Patient or Consultant)
    participant API as ASP.NET Core API
    participant DB as SQL Server
    participant ST as Stripe
    participant P as Other Participant (Patient/Consultant)

    U->>API: POST /appointments/{id}/cancel { reason }
    API->>API: Authenticate + Authorize (must be participant)
    API->>DB: SELECT Appointment + Payment
    alt Appointment.Status != Confirmed
        API-->>U: 409 Conflict (not cancellable)
    else Within 24h of ScheduledStartUtc
        API->>DB: BEGIN TRANSACTION
        API->>DB: UPDATE Appointment.Status = Cancelled
        API->>DB: UPDATE Appointment.CancellationReason = reason
        API->>DB: UPDATE Appointment.CancelledAt = Now
        API->>DB: COMMIT
        API->>API: Create Notification (to other participant)
        API-->>U: 200 OK { status: Cancelled, refund: null, message: "No refund — within 24h window" }
        API-->>P: SignalR: appointment cancelled
    else More than 24h before ScheduledStartUtc
        API->>ST: POST /v1/refunds { payment_intent: pi_xxx }
        alt Stripe refund creation fails
            ST-->>API: Error
            API->>DB: BEGIN TRANSACTION
            API->>DB: UPDATE Appointment.Status = Cancelled
            API->>DB: UPDATE Payment.RefundStatus = Failed
            API->>DB: COMMIT
            API-->>U: 200 OK { status: Cancelled, refund: { status: Failed }, message: "Refund failed. Contact support." }
        else Stripe refund created
            ST-->>API: Refund object (re_xxx)
            API->>DB: BEGIN TRANSACTION
            API->>DB: UPDATE Appointment.Status = Cancelled
            API->>DB: UPDATE Appointment.CancellationReason = reason
            API->>DB: UPDATE Appointment.CancelledAt = Now
            API->>DB: UPDATE Payment.StripeRefundId = re_xxx
            API->>DB: UPDATE Payment.RefundStatus = Pending
            API->>DB: COMMIT
            API-->>U: 200 OK { status: Cancelled, refund: { status: Pending }, message: "Refund initiated" }
            API-->>P: SignalR: appointment cancelled
        end
    end

    Note over ST,API: Later: Stripe webhook charge.refunded
    ST->>API: Webhook: charge.refunded
    API->>API: Verify signature + deduplicate (same atomic INSERT pattern)
    API->>DB: BEGIN TRANSACTION
    API->>DB: UPDATE Payment.RefundStatus = Succeeded
    API->>DB: UPDATE Payment.RefundedAt = Now
    API->>DB: Create Notification (to patient: "Refund processed")
    API->>DB: COMMIT
    API-->>P: SignalR: refund confirmed
```

### What this flow locks in

- **Same refund rule for both parties** — patient or consultant, the 24h rule is identical. This keeps the policy simple and fair.
- **Refund is synchronous initiation, asynchronous confirmation** — the API calls Stripe immediately during cancellation, but the actual money movement is confirmed later via webhook (same atomic pattern as payment confirmation).
- **Transaction boundary** — `Appointment.Status = Cancelled` + `Payment.RefundStatus = Pending` + `StripeRefundId` are committed together. A crash can't leave the appointment cancelled with no refund record.
- **Failed refund handling** — if Stripe refund creation fails (rare), `RefundStatus = Failed` is recorded and the user is informed. Manual support intervention is needed (acceptable for v1).
- **Earnings impact** — `GET /consultants/me/earnings` excludes `RefundStatus = Succeeded` payments, so refunded appointments never count as income.
- **Notification to both parties** — whoever cancels, the other participant receives a SignalR notification + in-app notification.


---

## 9. No-Show Detection

This diagram shows how the system automatically detects and handles no-show appointments using a background job, without requiring manual intervention.

### 9.1 Normal Call Start (for contrast)

```mermaid
sequenceDiagram
    participant P as Patient
    participant Hub as SignalR Hub
    participant API as ASP.NET Core API
    participant DB as SQL Server
    participant C as Consultant

    Note over P,C: ScheduledStartUtc = 09:00

    P->>Hub: JoinCall(appointmentId)
    Hub->>API: Validate participant + Status = Confirmed
    API->>DB: UPDATE Consultation.PatientJoinedAt = Now
    API->>DB: UPDATE Consultation.StartedAt = Now
    API->>DB: UPDATE Appointment.Status = InProgress
    API->>DB: COMMIT
    Hub->>C: IncomingCall notification

    C->>Hub: JoinCall(appointmentId)
    Hub->>API: Validate participant + Status = InProgress
    API->>DB: UPDATE Consultation.ConsultantJoinedAt = Now
    API->>DB: COMMIT

    Note over P,C: Call proceeds normally — no-show job will ignore this appointment because StartedAt is set
```

### 9.2 No-Show Detection — Background Job

```mermaid
sequenceDiagram
    participant BG as NoShowDetectionService<br/>(BackgroundService)
    participant DB as SQL Server
    participant ST as Stripe
    participant P as Patient
    participant C as Consultant

    Note over BG: Runs every 5 minutes

    BG->>DB: SELECT Appointments WHERE<br/>Status = Confirmed AND<br/>ScheduledStartUtc + 15min <= Now AND<br/>Consultation.StartedAt IS NULL

    alt Patient never joined, Consultant joined
        DB-->>BG: Appointment found (PatientJoinedAt = NULL, ConsultantJoinedAt = set)
        BG->>DB: BEGIN TRANSACTION
        BG->>DB: UPDATE Appointment.Status = NoShow
        BG->>DB: UPDATE Consultation.Status = PatientNoShow
        BG->>DB: INSERT Notification (to patient: "You missed your appointment")
        BG->>DB: INSERT Notification (to consultant: "Patient did not show")
        BG->>DB: COMMIT
        BG-->>P: SignalR: no-show notification
        BG-->>C: SignalR: no-show notification
        Note right of BG: No refund — patient forfeits payment

    else Consultant never joined, Patient joined
        DB-->>BG: Appointment found (PatientJoinedAt = set, ConsultantJoinedAt = NULL)
        BG->>DB: BEGIN TRANSACTION
        BG->>DB: UPDATE Appointment.Status = NoShow
        BG->>DB: UPDATE Consultation.Status = ConsultantNoShow
        BG->>DB: INSERT Notification (to patient: "Consultant did not show — refund initiated")
        BG->>DB: INSERT Notification (to consultant: "You missed your appointment")
        BG->>DB: UPDATE Payment.RefundStatus = Pending
        BG->>DB: COMMIT
        BG->>ST: POST /v1/refunds (payment_intent)
        ST-->>BG: Refund created (re_xxx)
        BG-->>P: SignalR: refund initiated
        BG-->>C: SignalR: no-show notification
        Note right of BG: Full refund — consultant at fault

    else Neither joined (mutual no-show)
        DB-->>BG: Appointment found (both NULL)
        BG->>DB: BEGIN TRANSACTION
        BG->>DB: UPDATE Appointment.Status = NoShow
        BG->>DB: UPDATE Consultation.Status = MutualNoShow
        BG->>DB: INSERT Notification (to both: "Appointment missed by both parties — no refund issued")
        BG->>DB: COMMIT
        BG-->>P: SignalR: no-show notification
        BG-->>C: SignalR: no-show notification
        Note right of BG: DECISION: No refund. Patient forfeits payment.<br/>v1 rule: no partial refunds, no platform liability.<br/>See Requirements §3.2.2 "Mutual No-Show — Explicit Decision".

    end

    Note over ST,BG: Later: Stripe webhook charge.refunded (for consultant no-show)
    ST->>BG: Webhook: charge.refunded
    BG->>DB: UPDATE Payment.RefundStatus = Succeeded, RefundedAt = Now
    BG-->>P: SignalR: refund confirmed
```

### 9.3 Late Join Attempt (after NoShow marked)

```mermaid
sequenceDiagram
    participant P as Patient
    participant Hub as SignalR Hub
    participant DB as SQL Server

    Note over P: 20 minutes after scheduled time
    P->>Hub: JoinCall(appointmentId)
    Hub->>DB: SELECT Appointment.Status
    DB-->>Hub: Status = NoShow
    Hub-->>P: Error: "This appointment has been marked as no-show and cannot be joined"
    Note right of P: Late join is blocked — state is final
```

### What this flow locks in

- **15-minute grace period** — hard cutoff. Adjustable in v2 based on analytics.
- **Fault attribution** — who joined and who didn't determines who is at fault, which drives the refund decision (Requirements §3.2.1).
- **Single transaction** — status update + notification + refund initiation (if applicable) are committed together. A crash can't leave the appointment as `NoShow` with no notification sent.
- **Late join protection** — once `NoShow` is set, `JoinCall` rejects permanently. The appointment state is final.
- **Background job is idempotent** — if the job runs twice on the same appointment (e.g., server restart), the second run finds `Status = NoShow` and skips it. No double-processing.
- **No manual intervention** — the entire flow is automatic from detection to refund (for consultant no-show).
- **Mutual no-show is deterministic** — never leaves the appointment in an ambiguous state. The background job commits `MutualNoShow` + no refund immediately. There is no "null return" or "pending decision" state.
