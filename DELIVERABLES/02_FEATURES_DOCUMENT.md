# VisionCart — Features Document

**Prepared** 25 August 2026
**Basis** Source code inspection of `dotnet/` — 84 C# files (24,599 lines), 55 Razor views, 22 application services, 9 client TypeScript modules

Every feature below was verified present in the source. Anything not
implemented is listed in §14 and marked accordingly.

---

## Contents

| § | Area |
| --- | --- |
| 1 | Catalogue & product |
| 2 | Virtual try-on |
| 3 | Prescriptions |
| 4 | Lens builder |
| 5 | Cart & pricing |
| 6 | Checkout & payment |
| 7 | Orders & fulfilment |
| 8 | Customer account |
| 9 | Appointments |
| 10 | Data protection |
| 11 | Back office |
| 12 | Import / export & media |
| 13 | Platform services |
| 14 | Not implemented |

---

## 1. Catalogue & product

### 1.1 Frame catalogue

| | |
| --- | --- |
| **Purpose** | Let a customer find a frame that suits them |
| **Screens** | `Frames/Index` |
| **Service** | `CatalogService.ListFramesAsync` |
| **Tables** | `Frame`, `FrameVariant`, `Brand`, `Category`, `FrameCategory` |
| **Roles** | Public |
| **Status** | Implemented |

**Filters:** free text (name, brand, SKU), wearer, shape, material, rim type,
brand, collection, frame size, plus sort order. Paginated.

**Business logic.** Only frames with status `active` are listed. Search runs
against the database collation (case-insensitive by default on SQL Server), a
behaviour pinned by test so a future collation change cannot silently break it.

### 1.2 Frame card

Shows image, brand, name, shape · material, **size in millimetres**
(lens–bridge–temple), colour swatches, price, sale badge, and a **Try it on**
action.

The card is not a single anchor: the title link is stretched across it and the
try-on control raised above, because a button nested in a link is invalid markup
and unreachable by keyboard. Verified reachable by Tab and visible when focused.

### 1.3 Frame detail

| | |
| --- | --- |
| **Screens** | `Frames/Detail` |
| **Tables** | `Frame`, `FrameVariant`, `ProductImage`, `LensOption` |
| **Status** | Implemented |

Colourway selection, measurements, lens mode selection (prescription / plain /
frame only), lens option selection, prescription entry, add to bag.

### 1.4 Deals

`Home/Deals` lists active promotions. Implemented.

---

## 2. Virtual try-on

| | |
| --- | --- |
| **Purpose** | Show a customer how a frame looks on their own face before ordering |
| **Screens** | `TryOn/Index`, `TryOn/Disabled` |
| **Client** | `ClientApp/tryon/` — `geometry.ts`, `pose.ts`, `smoothing.ts`, `studio.ts`, `faceLandmarker.ts` |
| **Tables** | `TryOnSession`, `TryOnSnapshot`, `FrameVariant` |
| **Roles** | Public |
| **Status** | Implemented |

### 2.1 Privacy architecture

**The face never leaves the browser.** Landmark detection, pose estimation,
geometry and rendering all run client-side. The MediaPipe model
(`/models/face_landmarker.task`) and WebAssembly runtime (`/wasm/*.wasm`) are
served from the application's own origin, and the Content-Security-Policy names
no external origin — which also blocks MediaPipe's own telemetry endpoint.

A photograph reaches the server **only** if the customer presses *Save to my file*.

### 2.2 Inputs

| Input | Notes |
| --- | --- |
| Live camera | `getUserMedia`, 1280×720 ideal, user-facing |
| Uploaded photo | Any browser-decodable image |
| Manual pupil placement | Fallback when detection fails |

### 2.3 Geometry

| Quantity | Source |
| --- | --- |
| Pupil positions | MediaPipe iris landmarks (468–477), falling back to eye corners |
| Roll | Angle of the eye line — a true image-plane angle |
| Yaw | Imbalance of cheek half-widths about the nose |
| Pitch | Nose position along the forehead–chin span |
| PD | Iris diameter reference (11.7 mm) |
| Face width | Cheekbone landmarks |

Yaw and pitch are **estimates from a flat projection**, not a solved 3D pose;
the code documents this explicitly. Confidence falls with angle and with a face
too small in frame.

### 2.4 Automatic fit

`autoFit` derives size, height and tilt from the measured face and the frame's
published dimensions:

- **Size** — the frame renders at its true manufactured width, using measured PD
  to convert millimetres to pixels. Clamped to 0.7–1.4× so a bad reading cannot
  distort the frame.
- **Height** — the pupil sits at 45% of lens depth, not the centre, because a
  wearer looks down far more than up.
- **Tilt** — the base solve already levels the eyes; measured roll is reported
  rather than double-applied.

### 2.5 Fit report

Reports frame width, size against face width with a verdict (**Good fit / Too
narrow / Too wide**), height adjustment and head tilt, with reasoning in plain
words. Re-runs when the customer changes frame.

### 2.6 Tracking quality

| Behaviour | Implementation |
| --- | --- |
| Jitter removal | One Euro filter — adapts cutoff to signal speed |
| Landmark loss | Last pose held ~320 ms, then faded over ~260 ms |
| Extreme angle | Frame foreshortens; a hint asks the customer to turn towards the camera |
| Stage shape | Canvas adopts the photo's aspect ratio |

### 2.7 Outputs

Download photo, save to patient file (when enabled), continue to lens selection.

---

## 3. Prescriptions

| | |
| --- | --- |
| **Purpose** | Capture and clinically verify a customer's prescription |
| **Screens** | Frame detail, `Admin/Patients/Detail`, `Guides/Prescription` |
| **Services** | `PrescriptionModels` (`Rx.Validate`, `Rx.Summarise`), `PatientAdminService` |
| **Tables** | `Prescription`, `Patient`, `OrderItem` |
| **Roles** | Customer enters; **Optician** verifies |
| **Status** | Implemented |

### 3.1 Fields

Per eye: sphere, cylinder, axis, add, prism, prism base, PD, segment height.
Plus issued date, expiry, prescriber, clinic, notes.

### 3.2 Validation

Enforced identically on the customer form, the optician screen and CSV import:

| Rule | Consequence if broken |
| --- | --- |
| Values must sit on a 0.25 D step | Rejected — no lab can make it |
| A cylinder must carry an axis | Rejected — cannot be ground |
| Axis is 1–180 | Rejected |

### 3.3 Verification workflow

1. Prescription arrives **pending verification** — from any source, including import.
2. It appears in the dashboard queue, oldest first.
3. An optician selects **Verify** or **Send & reject** with a reason.
4. The customer is emailed either way; a rejection carries the reason.
5. Only then can lenses be marked ready.

### 3.4 Immutability

A prescription used by an order cannot be deleted — enforced at the database
level with `DeleteBehavior.Restrict`, pinned by test. Changes create a new
version.

---

## 4. Lens builder

| | |
| --- | --- |
| **Tables** | `LensOption` |
| **Status** | Implemented |

Groups: lens type (single vision, bifocal, varifocal), index/thickness
(1.50–1.74), coatings (anti-reflective, scratch-resistant, blue filter,
photochromic), tints. Each option has a code, price and position. Retiring an
option hides it from customers without altering past orders.

---

## 5. Cart & pricing

| | |
| --- | --- |
| **Services** | `CartService`, `PricingService`, `PromotionService` |
| **Tables** | `Cart`, `CartItem`, `Promotion` |
| **Status** | Implemented |

### 5.1 Pricing authority

Prices are computed **only** in `PricingService`. The cart is re-priced from the
database on every read, so a tampered client payload cannot change what is
charged.

Money is an integer of minor units throughout. The DbContext refuses to build a
model in which any `*Minor` column is not an `int` — verified by
`SchemaMigrationTests`.

### 5.2 Promotions

| Type | Behaviour |
| --- | --- |
| Percentage off | Applied to subtotal |
| Fixed amount off | Applied to subtotal |
| Free delivery | Zeroes shipping |
| Buy one get one | Cheaper item discounted |

Settings: code (blank = automatic), minimum spend, maximum discount, stacking,
priority, start/end dates. A refused code returns the specific reason.

### 5.3 Cart scoping

Cart items are matched against the caller's own cart id, not by item id alone.

---

## 6. Checkout & payment

| | |
| --- | --- |
| **Services** | `CheckoutService`, `PaymentService`, `ShippingService` |
| **Tables** | `Order`, `OrderItem`, `Payment`, `Shipment`, `Address`, `ShippingRate` |
| **Status** | Implemented |

### 6.1 Flow

Contact details → delivery address → delivery method → payment method → place
order.

### 6.2 Transactional integrity

Order placement runs inside `ExecuteInTransactionAsync`, which makes the whole
transaction the retry unit for EF Core's connection resiliency and clears the
change tracker between attempts. Stock decrement, order creation, payment row
and prescription versioning either all happen or none do.

### 6.3 Payment providers

| Provider | Status |
| --- | --- |
| Cash on delivery | Implemented |
| Bank transfer | Implemented — instructions shown post-order |
| Stripe | Implemented (`Stripe.net` 52.3.0) |

Each degrades safely: a provider whose key is absent is not offered at checkout,
and the production startup guard refuses to boot if Stripe is listed without a
key.

### 6.4 Guest checkout

Supported. A patient file is created for guests — required for an optical order
to be remade or followed up.

---

## 7. Orders & fulfilment

| | |
| --- | --- |
| **Screens** | `Admin/Orders/Index`, `Admin/Orders/Detail`, `Order/Detail` |
| **Service** | `OrderAdminService` |
| **Status** | Implemented |

### 7.1 States

`pending → paid → in_lab → ready → shipped → delivered`, plus `cancelled` and
`refunded`. Payment status and fulfilment status are tracked **separately**,
because an order can be paid but unmade, or made but unpaid.

### 7.2 Staff actions

Mark as paid (with reference), update status, mark as shipped (courier +
tracking), record refund, lab stage transitions, internal notes.

### 7.3 Rules

- Cancelling returns frames to stock
- Lenses cannot be marked ready while the prescription is unverified
- Every state change is audited and emails the customer

### 7.4 Order snapshots

Order lines carry their own frozen copy of title, SKU, price, lens summary and
prescription — so a later catalogue change cannot rewrite history.

---

## 8. Customer account

| | |
| --- | --- |
| **Screens** | `Account/*`, `Addresses/*` |
| **Tables** | `AspNetUsers`, `Address`, `Order`, `Patient` |
| **Status** | Implemented |

Registration, sign-in, sign-out, forgotten password (single-use token, six-hour
expiry), password reset, order history.

### 8.1 Address book

`AddressService`. Add, edit, remove, set default.

| Rule | Reason |
| --- | --- |
| Every read is scoped to the owner | An id-only lookup is an enumeration hole |
| First address becomes default automatically | Otherwise checkout opens with nothing selected |
| Removal is a soft delete | Past orders reference the row they shipped to |
| Removing the default promotes another | Otherwise checkout opens empty |
| Maximum 20 per customer | Beyond that is data entry error |

---

## 9. Appointments

| | |
| --- | --- |
| **Screens** | `Appointments/Index`, `Appointments/Book`, `Admin/Diary/Index` |
| **Service** | `AppointmentService` |
| **Tables** | `Appointment`, `Patient` |
| **Status** | Implemented |

Kinds: eye test, fitting, collection, adjustment, follow-up.
Statuses: scheduled, completed, no-show, cancelled.

### 9.1 Rules

| Rule | Enforcement |
| --- | --- |
| No double-booking per clinician | Overlap check, not equality — a 60-minute slot blocks a booking 30 minutes in |
| Nothing in the past | Rejected |
| Opening hours 10:00–18:00, Mon–Sat | Rejected outside; Sunday offers no slots |
| Bookable 60 days ahead | Rejected beyond |
| Cannot be "seen" before it happened | Rejected |
| Cancelling frees the slot | Verified by test |

Taken slots are shown struck through rather than hidden, so a customer can see
how busy a day is. Email confirmation on booking, move and cancellation.

---

## 10. Data protection

| | |
| --- | --- |
| **Screens** | `Privacy/Index`, `Privacy/Raise`, `Admin/DataRequests/*` |
| **Service** | `DataSubjectService` |
| **Tables** | `DataSubjectRequest`, `Patient`, `Order`, `Address` |
| **Status** | Implemented |

### 10.1 Self-service export

`GET /account/privacy/download` returns JSON containing the account, patient
file, addresses, orders, prescriptions and appointments. Assembled by **explicit
projection**, not entity serialisation, so navigation properties cannot leak
another customer's rows. Verified by test. The download is itself audited.

### 10.2 Requests

Correction, erasure, export, restriction. Open to anonymous users — the right to
ask does not depend on being able to sign in. One open request per person per
kind. Acknowledged by email.

### 10.3 Erasure

**Pseudonymisation, not deletion.** Name, email, phone, date of birth and address
lines are destroyed across the patient file, account, orders and addresses; the
account is retired and its security stamp rotated, invalidating every session.

Prescriptions and order totals **survive** — a prescription is a medical record
and an order a financial one.

| Guard | Behaviour |
| --- | --- |
| Administrator only | Staff cannot action erasure |
| Typed confirmation | Must type `ERASE` |
| Open order blocks it | The courier needs somewhere to deliver |
| Single transaction | A half-erased person is worse than either outcome |

---

## 11. Back office

19 screens. Access requires the `StaffOnly` policy; the audit trail and settings
require `AdminOnly`.

| Screen | Purpose | Status |
| --- | --- | --- |
| Dashboard | What needs attention today — 8 tiles, prescription queue | Implemented |
| Orders | List, filter, detail, state transitions | Implemented |
| Patients | List, detail, edit, prescriptions, documents | Implemented |
| Diary | Clinic calendar, booking, attendance | Implemented |
| Frames | Catalogue management, colourways, stock | Implemented |
| Lenses | Lens options and prices | Implemented |
| Media | Upload, attach, delete | Implemented |
| Promotions | Offers and codes | Implemented |
| Delivery | Shipping rates | Implemented |
| Import | CSV import and export | Implemented |
| Data requests | Queue, detail, erasure | Implemented |
| Audit | Trail (admin only) | Implemented |
| Settings | Shop configuration (admin only) | Implemented |

### 11.1 Dashboard

Tiles: orders today, paid last 30 days, **awaiting payment**, in the lab, patient
files, live frames, **prescriptions to check**, **low stock lines**. Amber tiles
are work; the rest are information.

---

## 12. Import / export & media

### 12.1 CSV (`ImportService`, `ExportService`)

Four datasets: frames & stock, patients, prescriptions, orders.

The parser is a real CSV implementation, not `split(',')` — it handles quoted
fields containing commas, doubled quotes, fields spanning lines, CRLF, a UTF-8
BOM, missing trailing newline and blank lines. Eleven unit tests cover these.

| Behaviour | Detail |
| --- | --- |
| Dry run first | Reports without writing; verified by test |
| Errors by line number | `line = i + 2`, matching what Excel shows |
| Partial success | A bad row does not roll back good ones |
| Matching | Frames on `variant_sku`, patients on `file_no` |
| Money | Exported in **major** units so the file round-trips |
| Clinical exports | Audited as `export.patients` |

### 12.2 Media library (`MediaService`)

Bulk upload with EXIF auto-rotation, 2000 px cap, WebP conversion and
thumbnailing. Try-on overlays stay PNG to preserve transparency. Files upload one
at a time so a single corrupt image is reported by name.

**Two-phase delete:** `DeletedAt` → storage delete → `PurgedAt`, with an hourly
`MediaPurgeService` retrying failures up to 5 times — because the database and
the file system cannot be committed together. An image attached to a colourway
cannot be deleted.

---

## 13. Platform services

| Service | Purpose | Status |
| --- | --- | --- |
| `AuditService` | Every mutation of patient, price or order | Implemented |
| `EmailService` | Outbox pattern, drained by a hosted service | Implemented |
| `SettingsService` | Runtime configuration | Implemented |
| `MediaPurgeService` | Hourly orphan sweep | Implemented |
| `EmailOutboxService` | Background sender | Implemented |
| Health checks | `/health/live`, `/health/ready` | Implemented |
| File logging | Rolling, size-capped, 14-day retention | Implemented |
| Production guard | Refuses to start with development config | Implemented |

### 13.1 Email outbox

Mail is **queued, not sent inline**, so a slow SMTP server cannot stall checkout
and a failed confirmation is retried rather than lost. Drained inside the worker
process — shared hosting provides no external worker.

Templates: order confirmation, payment confirmation, order status, shipment,
prescription verified, prescription rejected, password reset, appointment, data
request.

---

## 14. Not implemented

Listed for completeness. These appear in the specification but are **not** in the
code.

| Item | Status | Note |
| --- | --- | --- |
| Try-on calibration admin UI | **Not implemented** | Anchors are per-variant data; there is no visual calibration screen |
| Favourites / wishlist | **Not implemented** | No entity or endpoint |
| Frame comparison | **Not implemented** | |
| Customer reviews | **Not implemented** | |
| Multi-currency | **Not implemented** | Single currency from settings |
| Multi-language | **Not implemented** | English only |
| Webhook handling for Stripe | **Partially implemented** | Provider present; webhook endpoint not verified in this build |
| Mobile responsive QA | **Partially implemented** | Layouts use responsive CSS; systematic testing at 360/390/430 px not completed |
| Admin redesign | **Partially implemented** | Functional and styled; the redesign pass covered the storefront |

---

*Cross-references: security controls in `03_SECURITY_FEATURES_DOCUMENT.md`;
verification evidence in `04_TEST_REPORT.md`; schema in
`08_DATABASE_DOCUMENTATION.md`.*
