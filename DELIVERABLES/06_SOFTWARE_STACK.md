# VisionCart — Software Stack

**Prepared** 25 August 2026
**Method** Versions read from `.csproj` files, `package.json` files and tool output. Anything unverifiable is marked **Not Verified**.

---

## 1. At a glance

VisionCart is a **server-rendered ASP.NET Core application** with a small
TypeScript client for the virtual try-on. There is no SPA framework and no
client-side router: pages are Razor, and JavaScript is used only where the
browser genuinely must compute something — namely face tracking.

This is deliberate. The application must deploy to shared Windows/IIS hosting
with no Node.js runtime on the server.

---

## 2. Complete stack

| Category | Technology | Version | Purpose |
| --- | --- | --- | --- |
| **Runtime** | .NET | 10.0 (`net10.0`) | Application runtime |
| **SDK** | .NET SDK | 10.0.400 | Build toolchain |
| **Language** | C# | 14 (SDK default) | Server-side application code |
| **Language** | TypeScript | via esbuild | Try-on client |
| **Web framework** | ASP.NET Core MVC | 10.0 | Controllers, routing, model binding |
| **View engine** | Razor | 10.0 | Server-rendered HTML |
| **ORM** | Entity Framework Core | 10.0.11 | Data access, migrations |
| **Database provider** | `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.11 | SQL Server dialect |
| **Design-time** | `Microsoft.EntityFrameworkCore.Design` | 10.0.11 | Migration tooling |
| **Database** | SQL Server | 2019+ / LocalDB in development | Persistence |
| **Authentication** | ASP.NET Core Identity | 10.0.11 | Accounts, passwords, lockout |
| **Identity stores** | `Microsoft.Extensions.Identity.Stores` | 10.0.11 | EF-backed Identity |
| **Validation** | FluentValidation | 12.1.1 | Input validation |
| **Validation DI** | `FluentValidation.DependencyInjectionExtensions` | 12.1.1 | Container integration |
| **Image processing** | SkiaSharp | 4.151.1 | Upload pipeline: rotate, resize, WebP |
| **Image natives** | `SkiaSharp.NativeAssets.Win32` | 4.151.1 | Windows native binaries |
| **Payments** | Stripe.net | 52.3.0 | Card payments |
| **Options** | `Microsoft.Extensions.Options` | 10.0.11 | Typed configuration |
| **Logging** | `Microsoft.Extensions.Logging.Abstractions` | 10.0.11 | Logging contracts |
| **Face tracking** | MediaPipe Tasks Vision | 0.10.x (vendored) | In-browser face landmarks |
| **Bundler** | esbuild | ^0.25.0 | Try-on client bundle |
| **Test framework** | xUnit | 2.9.3 | Unit and integration tests |
| **Test runner** | `xunit.runner.visualstudio` | 3.1.4 | Test discovery |
| **Test SDK** | `Microsoft.NET.Test.Sdk` | 17.14.1 | Test host |
| **HTTP test harness** | `Microsoft.AspNetCore.Mvc.Testing` | 10.0.11 | In-process HTTP tests |
| **Coverage** | coverlet.collector | 6.0.4 | Code coverage collection |
| **Client tests** | `node --test` | Node 24.15.0 | Geometry and pose tests |
| **Documentation tooling** | playwright-core | ^1.49.0 | Screenshot capture |
| **Asset tooling** | sharp | ^0.34.0 | Frame artwork generation |
| **Web server** | IIS + ASP.NET Core Module V2 | — | Production hosting |
| **Web server** | Kestrel | 10.0 | In-process HTTP server |
| **Package manager** | NuGet | via SDK | .NET dependencies |
| **Package manager** | npm | via Node | Build-time tooling only |

---

## 3. Licensing

Checked because two candidate libraries were rejected on licence grounds during
development.

| Package | Licence | Note |
| --- | --- | --- |
| SkiaSharp | MIT | Chosen over ImageSharp — ImageSharp ≥ 3.0 requires a paid commercial licence |
| xUnit | Apache 2.0 | Chosen over FluentAssertions — ≥ 8.0 requires a paid commercial licence |
| Stripe.net | Apache 2.0 | |
| FluentValidation | Apache 2.0 | |
| EF Core / ASP.NET Core | MIT | |
| MediaPipe Tasks Vision | Apache 2.0 | |
| esbuild | MIT | |

**No paid commercial licence is required to build, run or deploy this project.**

---

## 4. Architecture flow

### 4.1 Request lifecycle

```
   Browser
      │
      │ HTTPS
      ▼
┌─────────────────────────────────────────────┐
│ IIS  +  ASP.NET Core Module V2 (in-process) │
│ request limits · hidden segments · MIME map │
└─────────────────────────────────────────────┘
      │
      ▼
┌─────────────────────────────────────────────┐
│ ASP.NET Core middleware pipeline            │
│                                             │
│  forwarded headers → exception handler →    │
│  HSTS → HTTPS redirect → compression →      │
│  security headers → static files →          │
│  routing → rate limiter →                   │
│  authentication → authorisation             │
└─────────────────────────────────────────────┘
      │
      ▼
┌─────────────────────────────────────────────┐
│ VisionCart.Web                              │
│ Controllers · Razor views · view models     │
│ global antiforgery filter on every POST     │
└─────────────────────────────────────────────┘
      │
      ▼
┌─────────────────────────────────────────────┐
│ VisionCart.Application                      │
│ 22 services — all business rules            │
│ pricing · checkout · prescriptions ·        │
│ appointments · privacy · import/export      │
└─────────────────────────────────────────────┘
      │
      ▼
┌─────────────────────────────────────────────┐
│ VisionCart.Infrastructure                   │
│ EF Core · storage · email · payments ·      │
│ shipping · logging · health                 │
└─────────────────────────────────────────────┘
      │
      ▼
┌─────────────────────────────────────────────┐
│ SQL Server — 36 tables                      │
└─────────────────────────────────────────────┘

VisionCart.Domain sits beneath all of the above:
entities, constants, Money, Cuid. No dependencies.
```

### 4.2 Try-on data flow — entirely in the browser

```
  Camera / uploaded photo
        │
        │  (never leaves the device)
        ▼
  faceLandmarker.ts  ──  MediaPipe WASM, served from our own origin
        │
        │  478 landmarks
        ▼
  pose.ts        ──  roll · yaw · pitch · confidence
        │
        ▼
  smoothing.ts   ──  One Euro filter · loss handling
        │
        ▼
  geometry.ts    ──  solveTransform · autoFit · measureFace
        │
        ▼
  studio.ts      ──  canvas rendering
        │
        ▼
  Canvas         ──  frame drawn on the face

  Server involvement:  frame artwork + PD only, and only
                       if the customer presses "Save to my file".
```

### 4.3 Order lifecycle

```
  Browse → Try on → Choose lenses → Enter prescription → Bag
      │
      ▼
  Checkout ── single transaction ──┐
      │                            │ stock decrement
      │                            │ order + lines with snapshot
      │                            │ payment row
      │                            │ prescription version
      ▼                            └─ all or nothing
  Order placed  →  email queued (outbox)
      │
      ▼
  Optician verifies prescription  ──  rejected → customer emailed reason
      │ approved
      ▼
  In lab → Ready → Shipped → Delivered
```

---

## 5. Hosting requirements

| Requirement | Detail |
| --- | --- |
| Web server | IIS with the **.NET 10 Hosting Bundle** |
| Hosting model | In-process (`hostingModel="inprocess"`) |
| App pool | .NET CLR version **"No Managed Code"** |
| Platform | Windows, x64 (or x86 if the pool is 32-bit) |
| Database | SQL Server 2019 or later; `db_owner` on one database |
| Disk | ~80 MB for the application, plus uploads and logs |
| Writable folders | `wwwroot/uploads`, `logs` |
| SMTP | Any host, for order confirmations |
| **Not required** | Docker · Kubernetes · Linux · Node.js on the server · PostgreSQL · SQLite · any external worker process |

---

## 6. Build tooling

| Tool | Location | Purpose |
| --- | --- | --- |
| `dotnet build` / `dotnet test` | `dotnet/` | Compile and test |
| `publish.ps1` | `dotnet/` | Full release package — tests, assets, publish, strip |
| esbuild | `src/VisionCart.Web` | Bundles the try-on client to `wwwroot/js/tryon.js` |
| `tools/assets` | `dotnet/tools/` | Regenerates frame artwork; fetches the MediaPipe runtime |
| `tools/screenshots` | `dotnet/tools/` | Captures user-manual figures |
| `bench/benchmark.py` | `dotnet/bench/` | Response-time and throughput measurement |

**Node is a build-time dependency only.** The published output contains compiled
JavaScript; Node is never required to run or deploy.

---

## 7. Version verification

| Source | What it confirmed |
| --- | --- |
| `src/*/*.csproj`, `tests/*/*.csproj` | All NuGet package versions in §2 |
| `dotnet --version` | SDK 10.0.400 |
| `<TargetFramework>` | `net10.0` |
| `package.json` (three locations) | esbuild, playwright-core, sharp, MediaPipe |
| `node --version` | 24.15.0 |

**Not Verified:** the exact MediaPipe Tasks Vision patch version of the vendored
runtime — the bundle is committed rather than resolved, and carries no version
manifest. The `tools/assets` package pins `^0.10.21`.

---

*Cross-references: layer detail in `07_APPLICATION_ARCHITECTURE.md`; hosting
procedure in `09_DEPLOYMENT_DOCUMENT.md`.*
