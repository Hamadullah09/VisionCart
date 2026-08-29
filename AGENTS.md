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

`ClientApp/tryon/geometry.ts`, `pose.ts` and `smoothing.ts` are pure — no DOM,
no network — so they stay testable without a camera. Run them with
`node --test ClientApp/tryon/*.test.ts`.

If you change `DEFAULT_ANCHORS`, change the matching geometry in
`tools/assets/generate-frame-assets.mjs` too; they are two halves of one
contract, and the artwork is generated from it.

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
| `src/VisionCart.Web` | esbuild, bundles the try-on client |
| `tools/assets` | regenerates frame artwork and fetches the MediaPipe runtime |
| `tools/screenshots` | captures the figures in the user manual |
