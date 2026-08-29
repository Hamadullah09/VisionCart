# VisionCart — Application Architecture

**Prepared** 25 August 2026
**Basis** Source inspection: 84 C# files (24,599 lines), 55 Razor views, 9 client TypeScript modules

---

## 1. Architectural style

Four projects in a **layered (onion) arrangement**, with dependencies pointing
inward only:

```
        ┌──────────────────────────────────────┐
        │        VisionCart.Web                │  13 files ·  2,526 lines
        │  controllers · views · view models   │
        └──────────────────┬───────────────────┘
                           │ depends on
        ┌──────────────────▼───────────────────┐
        │     VisionCart.Infrastructure        │  21 files ·  9,793 lines
        │  EF Core · storage · email · payments│
        └──────────────────┬───────────────────┘
                           │ depends on
        ┌──────────────────▼───────────────────┐
        │      VisionCart.Application          │  27 files ·  6,929 lines
        │  22 services — all business rules    │
        └──────────────────┬───────────────────┘
                           │ depends on
        ┌──────────────────▼───────────────────┐
        │       VisionCart.Domain              │   8 files ·  1,421 lines
        │  entities · constants · Money · Cuid │
        │        NO DEPENDENCIES               │
        └──────────────────────────────────────┘
```

**Why it matters here:** business rules live in `Application` and are testable
without a web server or a browser. The 109 integration tests exercise the real
service graph against a real database, with no HTTP involved.

`Application` declares `IApplicationDbContext`; `Infrastructure` implements it.
The dependency is therefore inverted — the layer that owns the rules does not
depend on the layer that owns the database.

---

## 2. Directory structure

```
VisionCart/
├── AGENTS.md                     Engineering conventions (non-negotiables)
├── CLAUDE.md                     Includes AGENTS.md
├── README.md                     Project overview
├── MANUAL.pdf / .tex             Architecture and completion report
├── USER-MANUAL.pdf / .tex        57-page illustrated manual
│
└── dotnet/
    ├── VisionCart.slnx           Solution (XML format)
    ├── publish.ps1               Release packaging
    │
    ├── src/
    │   ├── VisionCart.Domain/
    │   │   ├── Entities/         People · Catalogue · Commerce · Platform
    │   │   ├── Constants/        VisionCartConstants.cs — every allowlist
    │   │   └── ValueObjects/     Money.cs · Cuid.cs
    │   │
    │   ├── VisionCart.Application/
    │   │   ├── Accounts/         AddressService
    │   │   ├── Admin/            Dashboard · Orders · Patients · Catalogue · Platform
    │   │   ├── Appointments/     AppointmentService
    │   │   ├── Carts/            CartService
    │   │   ├── Catalogue/        CatalogService
    │   │   ├── Checkout/         CheckoutService
    │   │   ├── Common/           IApplicationDbContext · Results
    │   │   ├── DataTransfer/     Csv · Import · Export
    │   │   ├── Email/            EmailService
    │   │   ├── Media/            MediaService
    │   │   ├── Patients/         PatientService
    │   │   ├── Payments/         PaymentService
    │   │   ├── Platform/         Audit · Settings
    │   │   ├── Prescriptions/    Rx validation and summarising
    │   │   ├── Pricing/          PricingService
    │   │   ├── Privacy/          DataSubjectService
    │   │   ├── Promotions/       PromotionService
    │   │   ├── Shipping/         ShippingService
    │   │   └── Storage/          IStorageProvider
    │   │
    │   ├── VisionCart.Infrastructure/
    │   │   ├── Persistence/
    │   │   │   ├── ApplicationDbContext.cs
    │   │   │   ├── Configurations/   4 files + Identity
    │   │   │   ├── Migrations/
    │   │   │   └── DatabaseSeeder.cs
    │   │   ├── Storage/          LocalStorageProvider · MediaPurgeService
    │   │   ├── Email/            SmtpEmailSender · EmailOutboxService
    │   │   ├── Payments/         Stripe · CashOnDelivery · BankTransfer
    │   │   ├── Shipping/         Carrier providers
    │   │   ├── Logging/          FileLogger — rolling, capped
    │   │   ├── Platform/         Health checks
    │   │   └── DependencyInjection.cs
    │   │
    │   └── VisionCart.Web/
    │       ├── Program.cs        Composition root and middleware pipeline
    │       ├── web.config        IIS configuration
    │       ├── Controllers/      8 controllers
    │       ├── Areas/Admin/      Back office (controllers + 19 views)
    │       ├── Views/            Storefront (29 views)
    │       ├── Models/           View models
    │       ├── Services/         ProductionConfigurationGuard
    │       ├── ClientApp/tryon/  TypeScript try-on client
    │       └── wwwroot/          CSS · JS · fonts · frames · models · wasm
    │
    ├── tests/
    │   ├── VisionCart.UnitTests/         92 tests
    │   └── VisionCart.IntegrationTests/  140 tests, incl. Http/ harness
    │
    ├── tools/
    │   ├── assets/               Frame artwork + MediaPipe fetch
    │   └── screenshots/          Manual figure capture
    │
    ├── bench/                    Response-time benchmark
    └── docs/                     8 engineering reports
```

---

## 3. Key files

| File | Why it matters |
| --- | --- |
| `Program.cs` | Composition root; the middleware order here **is** the security posture |
| `DependencyInjection.cs` | Every service registration; Identity policy |
| `ApplicationDbContext.cs` | Model building, the money guard, `ExecuteInTransactionAsync` |
| `VisionCartConstants.cs` | Every constrained-string allowlist — the schema stays portable because of this |
| `Money.cs` | The only place money conversion happens |
| `PricingService.cs` | The only place a price is decided |
| `ProductionConfigurationGuard.cs` | Refuses to boot with development configuration |
| `geometry.ts` | Frame placement mathematics; anchor contract |
| `pose.ts` | Head pose estimation |
| `smoothing.ts` | One Euro filter and tracking-loss handling |
| `web.config` | IIS hardening; `.task`/`.wasm` MIME registration |

---

## 4. Backend architecture

### 4.1 Controllers

8 controllers, 22 route prefixes.

| Controller | Routes | Access |
| --- | --- | --- |
| `StorefrontControllers` | `/`, `/frames`, `/cart`, `/checkout`, `/order`, `/error` | Public |
| `AccountController` | `/login`, `/register`, `/account`, password reset | Mixed |
| `AccountAreaControllers` | `/account/addresses`, `/account/appointments`, `/account/privacy` | Authenticated (privacy request: anonymous) |
| `TryOnController` | `/try-on` | Public |
| `GuidesController` | `/guides/*` | Public |
| `AdminControllers` | `/admin/*` — 9 areas | `StaffOnly` |
| `MediaAndDataControllers` | `/admin/media`, `/admin/import` | `StaffOnly` |
| `ClinicControllers` | `/admin/diary`, `/admin/data-requests` | `StaffOnly`; erasure `AdminOnly` |

### 4.2 Service layer

22 services. Two conventions throughout:

- **Result types, not exceptions,** for expected failures — `ActionResult`,
  `ActionResult<T>`, `FormResult`, `PagedResult<T>`. Exceptions are reserved for
  genuinely exceptional conditions.
- **Ownership scoping in the service,** not the controller. `AddressService`
  takes a `userId` and filters on it; there is no path that looks up by id alone.

### 4.3 Transactions

`IApplicationDbContext.ExecuteInTransactionAsync` wraps multi-table work.

It exists because EF Core's `EnableRetryOnFailure` is **incompatible with
user-initiated transactions** — the execution strategy must own the retry
boundary. This method makes the whole transaction the retry unit and clears the
change tracker before each attempt. Without it, order placement returned HTTP
500.

### 4.4 Background work

Two `IHostedService` implementations run inside the web worker, because shared
hosting provides no external worker:

| Service | Cadence | Purpose |
| --- | --- | --- |
| `EmailOutboxService` | Continuous | Drains queued mail |
| `MediaPurgeService` | Hourly | Sweeps orphaned storage objects, max 5 attempts |

---

## 5. Frontend architecture

### 5.1 Server-rendered

Razor views with tag helpers. No SPA framework, no client router, no state
library. Forms post; the server responds with HTML.

**Why:** the deployment target cannot run Node, and a shop of this size gains
nothing from client-side rendering that it does not pay for in complexity.

### 5.2 CSS

Two hand-written stylesheets — `visioncart.css` (storefront) and `admin.css`
(back office) — sharing one token system:

| Tokens | Steps |
| --- | --- |
| Type scale | 9 (`--text-display` … `--text-label`) |
| Spacing | 9 (`--space-1` … `--space-9`, 4 px base) |
| Colour | Warm neutrals, one bronze accent, semantic states kept separate from brand |
| Radius | 2 |
| Shadow | 2, warm and shallow |

Fonts (Fraunces, Inter) are **self-hosted** — consistent with the CSP naming no
external origin.

### 5.3 JavaScript

Four small external files. **No inline script anywhere** — the CSP forbids it,
and that is enforced by the browser rather than by convention.

| File | Purpose |
| --- | --- |
| `tryon.js` | Bundled try-on client (~21 KB) |
| `product.js` | Product page interactions |
| `admin.js` | Back-office helpers |
| `media-uploader.js` | Sequential per-file upload |

### 5.4 Try-on client

Layered, so the geometry is testable without a browser:

| Module | Responsibility | Tested |
| --- | --- | --- |
| `faceLandmarker.ts` | MediaPipe lifecycle | — |
| `pose.ts` | Roll, yaw, pitch, confidence | 24 tests |
| `smoothing.ts` | One Euro filter, loss handling | (in pose.test.ts) |
| `geometry.ts` | Transform solving, auto-fit, measurement | 42 tests |
| `studio.ts` | Canvas rendering, DOM, camera | — |
| `entry.ts` | Bootstrap | — |

`geometry.ts`, `pose.ts` and `smoothing.ts` touch no DOM and no network. That is
what makes 66 tests runnable with `node --test` and no camera.

---

## 6. Configuration

| File | Contains | Committed |
| --- | --- | --- |
| `appsettings.json` | Non-secret defaults | Yes |
| `appsettings.Development.json` | Local connection string, demo passwords | **No — git-ignored** |
| `appsettings.Development.example.json` | Same shape, blank passwords | Yes |
| `appsettings.Production.json` | Production defaults, **no secrets** | Yes |
| `web.config` | IIS configuration | Yes |
| Environment variables | Every production secret | n/a |

Double underscore maps to a configuration colon:
`ConnectionStrings__DefaultConnection` sets `ConnectionStrings:DefaultConnection`.

---

*Cross-references: schema in `08_DATABASE_DOCUMENTATION.md`; stack versions in
`06_SOFTWARE_STACK.md`.*
