# AI Telehealth Platform — Architecture Diagram (Detailed Reference)

**System Design Series · Document 2 of 5** (ERD → **Architecture** → Sequence Diagrams → API Contract → Security Deep-Dive)<br>**Updated:** Added scoped offline caching for the "My Appointments" screen (Hive, cache-aside, display-only — never used for actions like Join Call/Cancel); Replaced Stripe-only payments with `IPaymentProvider` abstraction (Stripe/Regional, per consultant country); Admin Dashboard elevated to v1.5 (Angular)
**Stack:** ASP.NET Core 10 (Clean Architecture, CQRS/MediatR) + Flutter (Clean Architecture, BLoC)

![Architecture Diagram](./architecture-diagram.png)

---

## 1. ERD vs. Architecture Diagram — What's the Difference?

These two diagrams answer two completely different questions:

- **ERD (Entity Relationship Diagram):** answers *"how is the data stored, and how are the tables related to each other?"* — it's about persistence.
- **Architecture Diagram:** answers *"what does the whole system consist of, and how does each part talk to the others?"* — it's about components and communication.

In short: the ERD tells you the shape of the data; the architecture diagram tells you the shape of the system. Both are needed, and they should stay consistent with each other — for example, the Architecture Diagram says "there is a relational database," and the ERD says "here is exactly what that database looks like."

## 2. The Big Picture

The diagram above is the final, agreed architecture for v1. Before breaking it down piece by piece, here is the reasoning behind why each component exists — understanding the *why* matters far more than memorizing the picture.

---

## 3. Flutter Applications (Client Layer)

The project has one Flutter app with two Flavors — a **Patient flavor** and a **Consultant flavor** — both built with Clean Architecture and BLoC. As a client, Flutter's responsibilities are:

- UI and user interaction
- Authentication flows (login, token storage, refresh)
- Calling the backend API
- Displaying appointments, chat, and notifications
- Initiating WebRTC calls
- Payment UI (collecting card details via Stripe's client SDK, never touching the secret key)

> **The one rule that matters most here: Flutter must never contain sensitive business rules.** The client can be wrong, outdated, or tampered with — so it should never be trusted to make decisions that affect data integrity or security.

**Wrong (Flutter decides):**
```
Flutter sees "slot looks free" -> assumes it can book it directly
```

**Correct (Backend decides):**
```
Flutter
  |
POST /appointments
  |
Backend: check availability, check conflicts, create appointment
  |
Response: confirmed or rejected
```

This directly matches the booking concurrency rules already locked in the Requirements Document and ERD — the backend, not the client, is the single source of truth for whether a slot is actually available.

> **Offline caching — scoped to one screen, not the whole app.** The app has no general offline-first requirement (video calls, chat, and live availability are inherently online-only), but the "My Appointments" list is an explicit exception: the last successfully fetched `GET /appointments` response is cached locally (Hive — lightweight enough for a flat list, no need for a full local SQL database like `drift`) so the screen isn't empty the moment connectivity drops. Implemented as a cache-aside pattern inside the `AppointmentRepository` (Data layer) — the Domain layer and BLoC never know caching exists:
> ```
> GET /appointments succeeds → overwrite the Hive cache + timestamp, return fresh data
> GET /appointments fails (no network) → read from Hive cache, return it with isStale = true + cachedAt
> ```
> **The same "backend decides, client displays" rule above still applies to cached data — it's shown, never acted on.** A stale cached appointment can be displayed (with a "last updated X ago" banner), but actions like *Join Call* or *Cancel* always require a fresh network check first; the client never lets a user act on data it can't currently verify against the backend. No other screen (chat, availability, earnings) gets this treatment — extending offline caching beyond this one screen without a concrete reason would be the same kind of premature complexity already rejected elsewhere in this document (Section 15).

> **Why one app with Flavors instead of two separate apps?** Both flavors share the same Clean Architecture codebase (domain, data, BLoC, SignalR client, API clients, models). The only difference is the entry point (`main_patient.dart` vs `main_consultant.dart`) and the presentation layer (screens, navigation). This eliminates code duplication, simplifies maintenance, and speeds up the MVP. Each flavor is built as a separate binary with its own `applicationId` and icon, so the stores treat them as distinct apps even though they share a single codebase.

---

## 3.5. Native SDK Layer (Kotlin / Swift)

While Flutter owns the UI and business logic, the Telehealth app requires platform-native capabilities that Dart cannot access directly. This is not a separate app — it's a **native plugin layer** inside the same Flutter project, communicating via Platform Channels.

```
Flutter (Dart)
    │
    ├── MethodChannel / EventChannel
    │
    ├── Android (Kotlin)
    │   ├── WebRTC media pipeline (Camera2 API)
    │   ├── Android Keystore (hardware-backed encryption keys)
    │   └── ConnectionService (background call handling)
    │
    └── iOS (Swift)
        ├── WebRTC media pipeline (AVFoundation)
        ├── Secure Enclave (hardware-backed encryption keys)
        └── CallKit (incoming call UI integration)
```

### Why not pure Flutter?

| Capability | Flutter Limitation | Native Solution |
|---|---|---|
| **Hardware video codecs** | `flutter_webrtc` uses software fallback for some codecs | Native Camera2/AVFoundation + hardware encoder |
| **Hardware-backed encryption** | `flutter_secure_storage` wraps Keychain/Keystore but doesn't expose key generation | Native Keystore/Secure Enclave APIs for AES key generation |
| **Background call UI** | Cannot show system-level incoming call screen | CallKit (iOS) / ConnectionService (Android) |
| **Echo cancellation tuning** | Limited control | Native WebRTC audio processing APIs |

### Communication Model

```
Flutter BLoC / UseCase
    │
    ▼
Domain Interface (Dart)
    │
    ├── IWebRTCNativeService
    ├── ISecureStorageNativeService
    └── ICallKitNativeService
    │
    ▼
Data Implementation (Dart)
    │
    ├── MethodChannel.invokeMethod()
    └── EventChannel.receiveBroadcastStream()
    │
    ▼
Native Plugin (Kotlin / Swift)
    │
    ├── WebRTC native library
    ├── Android Keystore / iOS Keychain
    └── OS Telephony Framework
```

**Critical rule:** Native code is a **capability provider**, not a decision maker. All business logic (when to start a call, what to encrypt, when to show incoming call UI) lives in Flutter's Domain Layer. Native code only executes what Flutter asks it to do.

---

## 4. ASP.NET Core 10 API (Application Layer)

This is the heart of the system. Every client — Patient flavor, Consultant flavor — talks only to this API. It owns:

- Authentication & Authorization
- Appointments & Scheduling
- Payments
- Consultations
- Chat (persistence side)
- Notifications
- AI orchestration

A single giant controller handling all of this would be unmanageable. Instead, the API is organized into feature modules, and each module internally follows Clean Architecture (Data / Application / Domain / Presentation), consistent with how the Restaurants API and Safaria backend were already structured:

```
ASP.NET Core
├── Auth
├── Users
├── Patients
├── Consultants
├── Appointments
├── Consultations
├── Payments
├── Chat
├── Notifications
└── AI
```

This is what's meant by a **Modular Monolith**: one deployable application, but internally organized as clearly separated modules — giving most of the maintainability benefits of microservices without the operational overhead of running and coordinating several separate services. That overhead is explicitly not worth taking on for this project (see Section 15).

---

## 5. SQL Server (Primary Database)

The system's single source of truth for durable data: Users, PatientProfiles, ConsultantProfiles, Appointments, Consultations, Payments, ChatMessages, AISummaries, AIJobs, Notifications, RefreshTokens, StripeWebhookEvents, and PayoutLedgerEntries. This maps directly onto the ERD built earlier — the relationship between the two documents is simple:

```
Architecture Diagram says:  "there is a relational database."
ERD says:                   "here is exactly what that database looks like."
```

---

## 6. Redis — Optional in v1

**Redis is not a replacement for SQL Server.** It serves a different purpose: fast, ephemeral data. In this project it would be used for two things:

- **Caching** — e.g. consultant availability lookups, so not every request hits SQL Server directly.
- **SignalR backplane** — needed only if the API scales to more than one instance, so real-time messages reach users connected to a different server instance.

```
            ┌── API #1
Flutter ────┼── API #2
            └── API #3
```

> Since v1 runs on a single instance, Redis is deliberately deferred rather than added "because production systems have it." This is a documented decision, not an oversight — see the Requirements Document, Section 8 ("Decisions Made") and Section 6 ("Later — v2+").

---

## 7. Background Workers

Not everything the API does should happen synchronously inside an HTTP request. The clearest example in this system is generating an AI consultation summary — a slow, unreliable, external-dependency call that should never block a client waiting on a response.

**What NOT to do:**
```
POST /consultations/{id}/complete
  |
Call AI -> wait 10 seconds -> generate summary -> return response   (BAD design)
```

**What the system actually does instead:**
```
API
  |
Create AIJob (Status = Pending)
  |
Return success immediately
  |
Hosted BackgroundService picks up the job asynchronously
  |
Process, retry on failure, persist result, notify
```

---

## 8. AI Job — Why the Pipeline is Designed This Way

The ERD already has an **AIJob** entity, which means the system was designed from the start to treat AI processing as its own asynchronous unit of work, not something bolted onto a controller:

```
Consultation Completed
        ↓
     AIJob (Status = Pending)
        ↓
  Hosted BackgroundService
        ↓
      IAIService
        ↓
     AI Provider (OpenAI)
        ↓
   Structured Output (validated)
        ↓
     AISummary persisted
```

This is a deliberately better design than a naive **Controller → OpenAI** call, because it survives provider slowness, transient failures, and — thanks to the `ProcessingStartedAt` stuck-job recovery logic already defined in the Requirements Document — even a server restart mid-job.

---

## 9. AI Service — The Abstraction Layer

The Application layer never talks to OpenAI directly. It talks to an interface:

```
IAIService
    │
    ├── OpenAIService           (v1 implementation)
    └── <FutureProviderService>  (swap-in later, e.g. Azure OpenAI)
```

The rest of the application only ever calls something like `GenerateSummary(...)` — it has no idea whether that's backed by OpenAI, Azure OpenAI, or anything else. This is exactly the `IAIService` → `Microsoft.Extensions.AI` → `OpenAI` chain already locked in as a v1 decision, and it's what gives the system the flexibility to change AI providers later without touching business logic.

---

## 10. Payments — `IPaymentProvider` Abstraction (Stripe + Regional)

> **⚠️ Revised (README v19):** the original version of this section assumed Stripe handles every payment. It can't — **Stripe Connect does not support payouts to Egypt**, and this platform's actual launch market is Egypt + the Arab world first. The API talks to an abstraction, not to Stripe directly, exactly the same pattern already used for `IAIService` — swap the concrete provider without touching business logic.

```
Flutter
  |
ASP.NET Core
  |
IPaymentProvider (abstraction)
  |
  ├── ConsultantProfile.Country is Stripe-supported (Saudi Arabia, UAE, ...)
  │       → StripeConnectPaymentProvider
  │
  └── ConsultantProfile.Country is Stripe-unsupported (Egypt, ...)
          → RegionalPaymentProvider (Paymob / PayTabs — exact choice is an implementation
            detail to confirm against each provider's actual API before building)
```

Flutter never handles either provider's secret key. The backend creates the payment intent/charge and returns only what the client needs to complete payment on its side. **Both providers' webhooks close the loop the same way — payment confirmation is never trusted from the client, regardless of which provider processed it:**

```
Stripe OR Regional Gateway
  |
Webhook (provider-specific event name)
  |
ASP.NET Core
  |
Update Payment.Status = Paid + Appointment.Status = Confirmed
```

**Where the two paths genuinely diverge — payout, not payment collection:**

```
Stripe path:                              Regional path:
Payment confirmed                         Payment confirmed
  |                                          |
Consultant's Stripe Connect balance       PayoutLedgerEntry created (Pending)
  |                                          |
Automatic weekly transfer                 Admin reviews + bank-transfers manually
(Stripe handles this entirely)              |
                                           Admin marks PayoutLedgerEntry as Paid
                                           (Admin Dashboard, Section 10.1)
```

> This is the same rule already established in the Requirements Document: **the system never trusts the Flutter client's claim that a payment succeeded** — only the provider's webhook is treated as the source of truth for payment confirmation. The same principle applies to refunds: the API initiates the refund synchronously during cancellation, but the final confirmation comes via webhook (Stripe path) or the regional gateway's equivalent mechanism (Regional path — must be verified against that gateway's actual refund API before implementation, since not every regional gateway supports automated refunds the way Stripe does).

**Refund flow (v1) — same policy, provider-aware execution:**
- Cancellations >24h before appointment → automatic full refund via the appointment's provider
- Cancellations <24h → no refund, payment kept
- `Payment.RefundStatus` tracks: `None` → `Pending` (initiated) → `Succeeded` (webhook confirmed) / `Failed` (rare)
- Earnings calculation excludes refunded payments, computed in `Payment.Currency` directly — never converted to USD before applying the 80/20 split (README §3.3.1)
- A refunded `Payment` on the Regional path must not leave a `Pending` `PayoutLedgerEntry` behind — the refund handler must reverse/cancel it in the same transaction

### 10.1 Admin Dashboard — Elevated to v1.5 (Angular, separate project)

The Regional payout path above has no automated settlement — something has to review pending `PayoutLedgerEntries` and confirm when a bank transfer has actually happened. That "something" is a minimal Admin Dashboard, which is why it's no longer purely a v2 deferral (Section 15 still defers the *full* admin suite — analytics, reporting, user management — this is strictly the minimum to make Section 10's Regional path operable):

- View pending `PayoutLedgerEntries`, grouped by consultant
- Mark a batch as settled after the admin performs the transfer outside the system
- Manual `ConsultantProfile.IsVerified` toggle (already a v1 requirement with no UI until now)
- **Stack: Angular** — a separate project/repo from the Flutter apps and the ASP.NET Core API, talking to the same API Contract (no admin-specific backend fork)

---

## 10.5. Email Service (SendGrid / AWS SES)

The system requires transactional email for two security-critical flows:
- **Email Verification** — sent automatically on registration
- **Password Reset** — sent on request via `/auth/forgot-password`

```
ASP.NET Core
      |
      ▼
Email Service (SendGrid / AWS SES / Mailgun)
      |
      ├── Verification emails
      └── Password reset emails
```

**Design principles:**
- The backend never trusts email delivery status for security decisions. Token expiry is time-based (1h for reset, 24h for verification), not delivery-status-based.
- Email sending is **fire-and-forget** from the API's perspective — failures are logged but don't block the HTTP response (the user already got their 201/200 response before email sending completes).
- In v1, email sending can be synchronous within the request (simplest) or queued via the same `BackgroundService` pattern used for AI jobs. The decision is an implementation detail; the architecture supports either.

---

## 11. WebRTC — A Fundamentally Different Kind of Connection

This part deserves special attention because it breaks the usual client-server-client pattern. The actual audio/video media must **not** flow through the ASP.NET Core API:

**Wrong (inefficient — API becomes a media relay):**
```
Flutter -> ASP.NET Core -> Flutter
```

**Correct (peer-to-peer media, backend only brokers the handshake):**
```
Patient device (Patient flavor)
       ↕
     WebRTC
       ↕
Consultant device (Consultant flavor)
```

The backend's role in a call is limited to:

- Signaling (exchanging Offer / Answer / ICE Candidates)
- Authentication & Authorization (only appointment participants may join)
- Call lifecycle events (started, ended, no-show, etc.)

```
Flutter
  |
SignalR / Signaling
  |
ASP.NET Core
  |
Exchange SDP / ICE
  |
Direct WebRTC connection (STUN, or TURN as fallback)
```

---

## 12. SignalR — Real-Time Delivery

SignalR is the backbone for both chat and WebRTC signaling. For a chat message specifically:

```
Patient
  |
SignalR
  |
ASP.NET Core
  |
Consultant
```

When the patient sends *"Hello Doctor"*, the server **persists first, then notifies** — never the reverse:

```
Patient
  |
SignalR
  |
ASP.NET Core
  |
Save message -> SQL Server (commit)
  |
Broadcast in real time -> Consultant
```

> **SQL Server is the persistence layer; SignalR is the real-time delivery layer.** This distinction matters — losing the SignalR connection should never mean losing the message, because the database write is committed before the broadcast is attempted. If the broadcast fails, the message is still safely stored and will be delivered on reconnect or via the notification history.

---

## 13. Notifications

There are two notification channels, serving different purposes:

- **In-app (v1):** SignalR → Flutter, delivered while the app is open.
- **Push (v2):** Firebase Cloud Messaging, delivered even when the app is closed.

```
Appointment confirmed
       ↓
Notification record (persisted)
       ↓
Push notification (v2, via FCM)
```

The `Notifications` table is the persistent history of what happened; the delivery channel (SignalR now, FCM later) is just how the person finds out about it. Keeping these separate means adding push notifications later doesn't require changing how notifications are generated or stored.

---

## 14. What Actually Belongs in an Architecture Diagram?

An architecture diagram answers one question: **"what is the system made of?"** — not "what are all its tables and classes?" That's a different, more detailed diagram (ERD / Component / Class design).

| Belongs in Architecture Diagram | Belongs in ERD / Component / Class Design instead |
|---|---|
| Client | Appointment |
| API | PatientProfile |
| Database | AIJob |
| Cache | Payment |
| Background Worker | (any individual table or class) |
| AI Provider | |
| Payment Provider | |
| Realtime layer | |

---

## 15. What This Diagram Deliberately Leaves Out

None of the following are recommended right now — not because they're bad technologies, but because adding them without a real, present need is premature complexity for a learning project:

- Kubernetes
- Kafka
- Microservices
- RabbitMQ / message brokers
- Multiple databases
- API Gateway
- Load Balancer
- Redis Cluster

The goal for v1 is a **Production-Ready Modular Monolith** — a design that's not so simple it skips real system-design learning, and not so over-engineered that time gets spent on infrastructure instead of backend architecture:

```
Flutter (Patient flavor / Consultant flavor)
    │
    ├── Dart Layer (UI, BLoC, Domain, API Client, SignalR Client)
    └── Native Layer (Kotlin/Swift — WebRTC, Crypto, CallKit, FCM Handler)
    │
ASP.NET Core Modular Monolith
    │
SQL Server
    │
Background Worker

  + SignalR, FCM (Push), IPaymentProvider (Stripe/Regional), WebRTC Signaling, AI Provider, Email Service
  + Redis (only once there's a real, present need for it)
```

---

## 16. Authentication & Authorization — Token Flow

The Requirements Document already says Flutter handles login/token storage/refresh, and the API handles authentication/authorization — but it's worth making the actual token flow explicit, especially since `RefreshTokens` already exists in the ERD:

```
Flutter
   │
   │ Access Token (JWT, 15min)
   ▼
ASP.NET Core
   │
   ├── JWT Authentication
   └── Authorization / RBAC (role + resource-ownership checks)
```

```
Flutter
   │
   │ Refresh Token (7 days, stored in Secure Storage)
   ▼
Auth Endpoint
   │
   ▼
Token Rotation + Reuse Detection
   │
   ├── Hash incoming token (SHA256) → compare against TokenHash
   ├── Generate new token → hash → store
   ├── Revoke old token (record ReplacedByTokenId, RevocationReason)
   └── Reuse Detection: if revoked token is used → delete entire family
```

**Security layers:**
- **Token hashing** — raw refresh token never touches the database; only `SHA256(Token)` is stored.
- **Rotation** — every refresh produces a new token and invalidates the old one. This limits the window of opportunity if a token is stolen.
- **Reuse detection** — if an attacker uses a stolen token *after* the legitimate user has already rotated it, the system detects this (the token is now marked revoked) and deletes **all** tokens in that family. Both attacker and legitimate user are forced to re-login — the legitimate user will notice and can secure their account.
- **Family binding** — all tokens from the same initial login share a `FamilyId`, enabling the nuclear option above.

No external Auth Server is needed for this. ASP.NET Core's own Identity + JWT handling is sufficient for v1 — this is purely about making an already-decided flow visible in the diagram, not a new component.

---

## 16.5. Security Architecture — Defense in Depth

The security design follows a layered approach, consistent with the Security Deep-Dive document:

```
┌─────────────────────────────────────────────┐
│  Layer 1: Endpoint Rate Limiting            │
│  (.NET Rate Limiter — per-IP)               │
├─────────────────────────────────────────────┤
│  Layer 2: Account Lockout                   │
│  (ASP.NET Core Identity — 5 fails/15min)    │
├─────────────────────────────────────────────┤
│  Layer 3: Progressive Delays                │
│  (Custom middleware — slows brute-force)    │
├─────────────────────────────────────────────┤
│  Layer 4: Anti-Enumeration                  │
│  (Same response regardless of existence)    │
├─────────────────────────────────────────────┤
│  Layer 5: Token Security                    │
│  (Hash-only storage, rotation, reuse det.)  │
└─────────────────────────────────────────────┘
```

**Key architectural decisions:**
- **No secrets in client** — Flutter never sees Stripe secret key, refresh token raw values, or password hashes.
- **Hash-only persistence** — refresh tokens, reset tokens, and verification tokens are all stored as `SHA256` hashes only. If the database is compromised, the attacker gets hashes, not usable tokens.
- **Generic error responses** — `401 Unauthorized` is identical whether the password is wrong, the account is locked, or the email doesn't exist. This prevents user enumeration and credential stuffing reconnaissance.
- **Transaction safety** — any security-critical multi-write operation (password change + session invalidation, reset + token consumption + session kill) happens in a single database transaction.

---

## 17. Observability (v1 Baseline)

If the goal is learning patterns that transfer to real production work, this is arguably the most valuable addition — and the easiest to skip by accident:

```
ASP.NET Core
      │
      ├── Structured Logging
      ├── Health Checks
      ├── Metrics
      └── Error Tracking
```

This does **not** mean standing up Grafana + Prometheus + ELK in v1. A realistic v1 baseline is:

- ASP.NET Core's built-in structured logging
- Global exception handling middleware
- Health check endpoints (`/health`)
- A correlation ID per request (useful for tracing a request across the API → BackgroundService → AI pipeline)
- A hosted error-tracking service (e.g. Sentry or Application Insights) for anything beyond local logs

The concept mattering more than the specific tool is the point — the system should never be a black box once something goes wrong.

---

## 18. Notification Channel Abstraction (v1: SignalR + FCM)

Notifications in v1 use **both** channels — not one or the other. The abstraction was designed from the start to support multiple channels:

```
INotificationService
       │
       ├── InApp   (v1 — SignalR, real-time when app is open)
       ├── Push    (v1 — FCM, background/closed app delivery)
       ├── Email   (v2+)
       └── SMS     (v2+)
```

**Why both in v1:**
- **SignalR** delivers instantly when the app is open (chat messages, call events).
- **FCM** delivers when the app is backgrounded or killed (incoming call alerts, appointment reminders, booking confirmations).
- A Telehealth app where the patient misses a call because the app was closed is a **critical failure**, not a UX inconvenience.

**Implementation:**
- `NotificationCreatedDomainEvent` is raised after the `Notification` entity is persisted.
- `InAppNotificationHandler` reacts: sends via SignalR if the user is connected.
- `PushNotificationHandler` reacts: sends via FCM to all registered device tokens for that user.
- Both handlers are independent — one failing does not block the other.

**Native layer responsibility:**
- FCM messages are received by the native layer (Kotlin/Swift) when the app is backgrounded.
- `type: "incoming_call"` → trigger CallKit/ConnectionService immediately.
- `type: "appointment_reminder"` → show standard system notification.
- When the user taps the notification, the app launches and Flutter handles deep-linking.

**Device token lifecycle:**
- Token obtained from `FirebaseMessaging.instance.getToken()` → sent to backend via `POST /notifications/device-token`.
- Token stored in `UserDeviceTokens` (unique per user + platform, multiple devices supported).
- Token removed on logout via `DELETE /notifications/device-token`.
- Tokens are refreshed periodically by FCM; Flutter re-registers the new token automatically.

---

## 19. Storage (Object/File Storage) — Explicitly Deferred

The Requirements Document already defers `MedicalRecords` and `Attachments` to v2, so file/object storage should **not** be added to the v1 architecture just for completeness. Adding infrastructure ahead of an actual requirement is exactly the kind of premature complexity Section 15 already warns against.

For reference, once v2 needs it:
```
ASP.NET Core
      │
      ▼
Object Storage
      │
      ├── Medical Documents
      ├── Attachments
      └── Images
```
This is documented here only so the shape is known in advance — it is **not** part of the v1 diagram.

---

## 20. Message Queue — A Future Evolution Path, Not a v1 Component

The current pipeline —

```
ASP.NET Core
      ↓
Hosted BackgroundService
      ↓
AIJob
```

— is the right fit for v1, and matches the existing decision to avoid Hangfire or a message broker for now (Requirements Document, Section 8). If AI workload ever grows large enough to need real queuing/distribution:

```
API
 ↓
Queue (RabbitMQ / Azure Service Bus / SQS, depending on environment)
 ↓
Worker
 ↓
AI
```

This is a **later evolution path**, not a v1 component — recorded here so the migration direction is clear if it's ever needed, without pulling the infrastructure in early.

---

## 21. Deployment Architecture (Separate from Logical Architecture)

Everything above is the **Logical Architecture** — what the system is made of and how components communicate. A full system-design pass also benefits from a small, separate **Deployment Architecture** diagram — how those same components actually get deployed and run:

![Deployment Architecture](./deployment-diagram.png)

The same modular monolith is deployed as a single instance in v1 across three environments:

```
Environment
├── Development   (local / debug builds)
├── Staging       (pre-release testing)
└── Production    (live users)
```

Keeping this diagram separate from the logical one matters: the *logical* architecture (modules, data flow, abstractions) stays the same across environments — only *how* and *where* it's deployed changes. Conflating the two makes both diagrams harder to read.

---

## 22. WebRTC — STUN vs. TURN, Made Explicit

The original diagram already showed STUN/TURN correctly, but it's worth stating the distinction plainly since it's easy to gloss over:

```
                WebRTC
               /      \
             STUN     TURN
              │         │
              └── Media ┘
```

- **STUN** — used first, attempts to establish a **direct peer-to-peer** connection by discovering each client's public-facing address (NAT traversal).
- **TURN** — only used as a **fallback** when a direct P2P connection isn't possible (e.g. restrictive NAT/firewall on one side); traffic is relayed through the TURN server instead of going directly between clients.

In both cases, ASP.NET Core never carries the audio/video itself — its only role stays signaling (exchanging the offer/answer/ICE candidates so the two clients can find each other), consistent with Section 11.

---

## 23. Updated Assessment

Rating the architecture as it now stands, purely from a "learning production-grade system design" lens: **8.5–9/10.**

The gap was never a missing *technology* — it was under-specifying a few *concerns*:

- 🔐 Authentication / Authorization flow — now explicit (Section 16)
- 📊 Observability — now included as a v1 baseline (Section 17)
- 📬 Notification channel abstraction — documented as a future-ready shape (Section 18)
- 📁 Storage — explicitly deferred, not added prematurely (Section 19)
- 🚀 Queue — documented as a future evolution path, not a v1 component (Section 20)
- 🌐 Deployment architecture — now a separate diagram (Section 21)
- STUN vs. TURN — made explicit (Section 22)

The things still deliberately excluded remain the same, and for the same reason: **Kubernetes, Kafka, RabbitMQ, Microservices, API Gateway, Load Balancer, Redis Cluster.** The project's own decision — a v1 Production-Ready Modular Monolith — hasn't changed; these additions sharpen it rather than complicate it.

---

**Next in the System Design series:**
1. The full **API Contract** — endpoint list, request/response shapes, validation, authorization, status codes, and SignalR Hub contracts.
2. The **`IAIService` interface contract** — internal abstraction signatures and DTOs, separate from the public API surface.
3. **Chat encryption decision** — application-layer vs. SQL Server column-level encryption.
4. **Implementation** — begin coding against the finalized contracts above.
