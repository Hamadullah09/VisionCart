# VisionCart — conventions

Prescription eyewear shop, on ASP.NET Core 10 and SQL Server. Read
`dotnet/README.md` first; it explains how to run it. `dotnet/docs/` covers the
try-on geometry, the provider adapters and the patient-data rules.

The application lives entirely in `dotnet/`. The Next.js original it was ported
from has been removed — it is still in git history and in the backup beside the
repository if you need to consult it.

## Non-negotiables

- **Money is an integer of minor units** (paisa/cents) everywhere. Staff type
  major units in forms; convert once at the edge with `Money.ToMinor` /
  `Money.FromMinor`. Never do floating-point arithmetic on a price. The
  DbContext refuses to build a model where a `*Minor` column is not an `int`.
- **Prices are decided only in `Application/Pricing/PricingService.cs`.** Views
  display; they never compute. The cart is re-priced from the database on every
  read so a tampered client payload cannot change what is charged.
- **Prescriptions are immutable once used.** Create a new version; never edit a
  prescription attached to a past order. Order lines keep their own snapshot.
- **Diopter inputs are dropdowns**, stepping 0.25 D, from
  `Domain/Constants/VisionCartConstants.cs`. A free-text field here becomes a
  lab remake.
- **Every mutation of a patient record, price or order calls `IAuditService`** —
  but never put clinical values in the audit detail.
- **The schema stays portable** across SQL Server, Postgres and MySQL: no
  enums, no arrays, no native JSON. Constrained strings are validated against
  the constants file; JSON is a stringified column.

## Where things go

| Layer | Holds |
| --- | --- |
| `VisionCart.Domain` | Entities, constants, `Money`, `Cuid`. No dependencies. |
| `VisionCart.Application` | Services. All business rules live here. |
| `VisionCart.Infrastructure` | EF Core, storage, email, payment and shipping adapters. |
| `VisionCart.Web` | Controllers, Razor views, the try-on client. |

- Staff-only controllers derive from `AdminControllerBase`, which carries the
  `StaffOnly` policy. Administrator-only actions add `AdminOnly` explicitly.
- New payment or shipping providers are adapters in `Infrastructure/Payments`
  and `Infrastructure/Shipping`. Both must degrade safely when their key is
  absent.

## Try-on

**A frame is drawn at the size it is made in, not stretched onto the pupils.**
The wearer's PD is what gives a photograph a scale:

    pixels per millimetre = pupil separation in pixels / PD in millimetres

From there `fit.ts` draws the frame at its recorded `TotalWidthMm`, seats it by
`LensHeightMm`, and reports the gap between the frame's lens centres and the
wearer's eyes as decentration rather than removing it. Two people photographed
at the same pupil separation but with different PDs are at different distances,
so the same frame comes out different sizes on each — that is correct, and
`fit.test.ts` pins it.

`fit.ts`, `geometry.ts`, `pose.ts` and `smoothing.ts` are pure — no DOM, no
network — so they stay testable without a camera. Run them with
`node --test ClientApp/tryon/*.test.ts`.

**Never branch on a frame's identity.** No `if (variantId === …)`, no per-frame
multiplier. If a frame needs a correction it belongs in its calibration row,
and there is a screen for setting that: `/admin/frames/{id}/variants/{v}/calibrate`.

Six numbers say where a frame sits inside its own artwork — two lens centres,
the two edges of the frame front, the top and bottom of the lens opening. They
are what lets the renderer tell frame from padding. `tools/assets/frames.json`
holds the millimetres, the generator draws to them and emits the matching
calibration, and the seeder writes both; artwork and data therefore agree by
construction. `TryOnReadiness` (C#) and `checkFrameData` (TS) both read the
scale of an asset three independent ways and complain when they disagree —
if you change a threshold, change it in both.

The customer's face never leaves the browser. The MediaPipe model and its
WebAssembly runtime are served from our own origin rather than a CDN precisely
so that stays true, and the Content-Security-Policy names no external origin —
which is also what blocks MediaPipe's own telemetry.

Still-photo rendering draws synchronously from an effect. Do not move it back to
`requestAnimationFrame`: rAF is suspended in a background tab and the canvas
would stay blank.

## Node

Node is a **build-time dependency only** — never required to run or deploy.

| Where | For |
| --- | --- |
| `src/VisionCart.Web` | esbuild, bundles the try-on client and the calibration screen (`npm run build`) |
| `tools/assets` | regenerates frame artwork from `frames.json` and fetches the MediaPipe runtime |
| `tools/screenshots` | captures the figures in the user manual |
