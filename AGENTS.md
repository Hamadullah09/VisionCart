<!-- BEGIN:nextjs-agent-rules -->

# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->

# VisionCart — conventions

Prescription eyewear shop. Read `README.md` first; it explains the try-on
geometry, the provider adapters and the patient-data rules.

## Non-negotiables

- **Money is an integer of minor units** (paisa/cents) everywhere. Staff type
  major units in forms; convert once at the edge with `toMinor`/`fromMinor`.
  Never do float arithmetic on a price.
- **Prices are decided only in `src/lib/pricing.ts`.** Components display; they
  never compute. The cart is re-priced from the database on every read so a
  tampered client payload cannot change what is charged.
- **Prescriptions are immutable once used.** Create a new version; never edit a
  prescription attached to a past order. Order lines keep their own snapshot.
- **Diopter inputs are dropdowns**, stepping 0.25 D, from `src/lib/constants.ts`.
  A free-text field here becomes a lab remake.
- **Every mutation of a patient record, price or order calls `audit()`** — but
  never put clinical values in the audit detail.
- **The schema stays portable** across SQLite, Postgres and MySQL: no enums, no
  arrays, no native JSON. Constrained strings are validated against
  `src/lib/constants.ts`; JSON is a stringified column.

## Where things go

- Mutations are **server actions** in `src/app/actions/`. Route handlers in
  `src/app/api/` are only for multipart uploads, CSV and webhooks.
- Staff-only server actions start with the `staff()` guard; pages use
  `requireStaff()` / `requireAdmin()`.
- New payment or shipping providers are adapters in `src/lib/payments.ts` /
  `src/lib/shipping.ts`. Both must degrade safely when their key is absent.

## Try-on

`src/lib/tryon.ts` is pure — no DOM, no server imports — so it stays testable.
If you change `DEFAULT_ANCHORS`, change the matching geometry constants in
`scripts/generate-frame-assets.mjs` too; they are two halves of one contract.

Still-photo rendering draws synchronously from an effect. Do not move it back
to `requestAnimationFrame`: rAF is suspended in a background tab and the canvas
would stay blank.
