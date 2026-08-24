# VisionCart — Domain Services and the Purchase Vertical Slice

**Phase 3–4 progress report.** Follows on from `01-migration-inventory.md`.

The customer purchase path now runs end to end on ASP.NET Core 10 + SQL Server:

```
Home → Catalogue → Product → Lens builder → Prescription → Cart → Promo → Checkout → Order
```

---

## 1. What was built

### Application layer — 11 services

| Service | Ported from | Notes |
| --- | --- | --- |
| `Rx` | `src/lib/rx.ts` | Prescription validation. Pure; no database. |
| `PricingService` | `src/lib/pricing.ts` | The only place a price is decided. |
| `PromotionService` | `src/lib/promotions.ts` | Six deal kinds, nine conditions, stacking rule. |
| `CartService` | `cart.ts` + `actions/cart.ts` | Re-prices from the database on every read. |
| `CheckoutService` | `actions/checkout.ts` | The 15-step order path, transactional. |
| `CatalogService` | `src/lib/catalog.ts` | Projected and paged. |
| `ShippingService` | `src/lib/shipping.ts` | Rate table + 2 carriers + safe fallback. |
| `PaymentService` | `src/lib/payments.ts` | 3 methods + webhook idempotency. |
| `PatientService` | patient helpers in `auth.ts` | File numbering, guest matching. |
| `SettingsService` | `src/lib/settings.ts` | Cached, invalidated on write. |
| `AuditService` | `src/lib/audit.ts` | Never takes down the operation it records. |

Provider adapters in Infrastructure: `StripePaymentProvider` (hosted checkout +
refunds), `ShippoShippingProvider` (rates + label purchase),
`EasyPostShippingProvider` (rates), `CashOnDeliveryProvider`,
`BankTransferProvider`.

### Web layer

Six controllers (Home, Frames, Cart, Checkout, Order, Guides, Error), eleven
Razor views, a shared layout and a hand-authored stylesheet reproducing the
legacy design tokens.

**No Node build step at deploy time.** Tailwind was not carried over as a build
dependency; the stylesheet is plain CSS using the same palette and 1100px content
column. Node remains a *development* tool only (it runs the try-on geometry
tests). This was a deliberate simplification for shared IIS hosting — see
Known limitations.

---

## 2. Bugs found and fixed during the slice

Each of these was caught by running the thing, not by reading it.

| Bug | How it surfaced | Fix |
| --- | --- | --- |
| **Connection resiliency broke every transaction.** `EnableRetryOnFailure`, added for shared hosting, is incompatible with a hand-rolled transaction — EF refuses, because a retry would replay only part of it. Order placement returned HTTP 500. | Placing an order | `IApplicationDbContext.ExecuteInTransactionAsync` makes the whole transaction the retry unit. The change tracker is cleared before each attempt, so a retry cannot re-insert entities left over from the failed one. |
| **The try-on model 404'd.** ASP.NET Core refuses to serve unknown MIME types, and `.task` is unknown. The face model would never load and the mirror would silently fall back to manual pupil placement on every visit. | Route sweep | Explicit `FileExtensionContentTypeProvider` mapping for `.task` and `.wasm`. |
| **Lens builder steps rendered alphabetically** — Coatings, Extras, Thickness, Tint, Type, Usage — instead of the wizard order a customer walks. The database returns options ordered by group name. | Comparing the rendered page against the legacy | `LensGroups.OrderOf()` in Domain, used by the view model. |
| **`Restrict` vs `NoAction` on order-line FKs.** `NoAction` leaves EF's client-side fixup in place: with the line tracked, EF nulls `PrescriptionId` and issues an `UPDATE` *before* the `DELETE`, severing the clinical link without ever touching the constraint. | A schema test that expected a failure and got a success | `DeleteBehavior.Restrict`, plus a test asserting via raw SQL so it holds for maintenance scripts too. |
| **Identity key length mismatch** (SQL Server error 1753). | First migration apply | All five Identity child tables narrowed to match the 30-char cuid keys. |

---

## 3. Verified behaviour

Compared directly against the running legacy application:

| Check | Legacy | ASP.NET Core |
| --- | --- | --- |
| Frame price | Rs.6,500.00 | Rs.6,500.00 |
| Delivery (table rate) | Rs.300.00 | Rs.300.00 |
| `WELCOME15` discount | − Rs.975.00 | − Rs.975.00 |
| Cart total | Rs.5,825.00 | Rs.5,825.00 |
| Lens options | 24 across 6 groups | 24 across 6 groups |
| Frames seeded | 10 frames, 30 colourways | 10 frames, 30 colourways |
| Order number format | `VC-2026-000001` | `VC-2026-000001` |

Routes confirmed live: `/`, `/frames`, `/frames/{slug}`, `/deals`, `/cart`,
`/checkout`, `/order/{orderNo}`, all four `/guides/*` pages (which **404'd in the
legacy application**), `/error/{code}`, and the try-on assets
(`face_landmarker.task` 3.76 MB, `vision_wasm_internal.wasm` 11.76 MB) served
from the application itself with correct content types.

Security headers present on every response: CSP (allowing `wasm-unsafe-eval` for
MediaPipe but **no external origins**, which is what keeps the face model local),
`X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`,
and `Permissions-Policy: camera=(self)`.

---

## 4. Test suite — 92 tests, all passing

| Suite | Count | Runs against |
| --- | --- | --- |
| Unit (`VisionCart.UnitTests`) | 26 | Money arithmetic, cuid generation |
| Integration (`VisionCart.IntegrationTests`) | 37 | Real SQL Server |
| Try-on geometry (`node --test`) | 29 | The browser TypeScript, unmodified |

Integration breaks down as 9 schema tests, 15 checkout side-effect tests and 13
promotion rule tests. The checkout tests drive the **real service graph** — only
the cart cookie and the current user are test doubles, because there is no HTTP
request in scope. They assert what a green redirect cannot: stock reserved, the
guest's patient file created and reused on return, the typed prescription
promoted to a versioned record marked *pending verification*, the frozen snapshot
written, the promotion counted, the bag consumed but the cart kept, and the audit
entry recorded **without clinical values in it**.

---

## 5. Known limitations of the slice

Stated plainly rather than left to be discovered.

- **Tailwind was not carried over.** The stylesheet is hand-authored CSS matching
  the legacy design tokens. Visually very close, not pixel-identical. Restoring
  Tailwind is possible (compile to static CSS at build time) at the cost of a
  Node build step; it was traded away for deployment simplicity.
- **The try-on studio UI is not yet migrated.** The geometry module is moved and
  fully tested, and the model and runtime are served correctly, but the 730-line
  canvas component has not been rewritten as a TypeScript module. `/try-on` is
  still linked from the navigation and will 404 until it is.
- **No authentication UI.** Identity, roles, policies, lockout and reset tokens
  are all configured, but there is no sign-in, registration or password-reset
  page yet, so `/login` 404s. Guest checkout — the path the slice proves — does
  not need one.
- **The back office is not migrated.** All 16 screens remain outstanding.
- **Email is not wired.** The `OutboxEmail` table and its indexes exist; nothing
  writes to or drains it yet.
- **Rate limiting is configured but only applied to checkout.** The `auth` and
  `upload` policies are defined and unused until those endpoints exist.
- **Stripe and the carriers are implemented but untested against live keys.**
  No credentials are available in this environment. The code paths are real, not
  stubs, and both degrade correctly when a key is absent — which *is* tested.
