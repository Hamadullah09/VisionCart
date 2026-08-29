# VisionCart — Master Project Specification

**Merged development requirements**
Prepared 25 August 2026 · Consolidated from all specification documents found in the project

---

## About this document

This is a single consolidated specification, merged from every requirement
document, instruction file and convention note found in the project directory:

| Source | Contributed |
| --- | --- |
| `AGENTS.md` / `CLAUDE.md` | Engineering non-negotiables, layer rules, try-on constraints |
| `dotnet/docs/01-migration-inventory.md` … `08` | Migration decisions, phase requirements |
| `PROMPT.docx` | Original brief |
| `dotnet/docs/07-deployment.md` | Hosting constraints |
| In-repository specifications | Migration brief (33 sections), redesign brief (48 sections) |

Duplicate and contradictory instructions have been reconciled. Where two
sources disagreed, the one matching the **implemented code** was kept, since the
code is the authority on what was actually required.

---

## 1. Product definition

VisionCart is a **prescription eyewear retailer**, not a general e-commerce site
that happens to sell glasses. Every requirement below follows from that: the
clinical record is a primary entity, not an add-on.

The system serves four audiences:

| Audience | Needs |
| --- | --- |
| Customer | Browse frames, try them on, enter a prescription, order |
| Staff | Process orders, manage the catalogue and the diary |
| Optician | Verify prescriptions before anything reaches the lab |
| Administrator | Settings, audit trail, data-subject erasure |

---

## 2. Hosting constraint — the governing requirement

The application **must deploy to shared Windows/IIS hosting** (myASP.NET class).

The deployment process must **not** require:

- Docker or Kubernetes
- Linux
- A continuously running Node.js server
- PostgreSQL
- Prisma
- SQLite in production

This constraint drove the platform choice and overrides convenience. Node is
permitted **at build time only** — never to run or deploy.

---

## 3. Non-negotiable engineering rules

These are invariants, not preferences. Each exists because breaking it causes a
specific, expensive failure.

### 3.1 Money

Money is an **integer of minor units** (paisa/cents) everywhere. Staff type major
units in forms; conversion happens once at the edge. No floating-point
arithmetic on a price, ever.

> *Rationale:* repeated float arithmetic across discounts, tax and shipping
> drifts by rupees on large orders, and the drift is not reproducible.

### 3.2 Pricing authority

Prices are decided in **one place** — the pricing service. Views display; they
never compute. The cart is **re-priced from the database on every read**, so a
tampered client payload cannot change what is charged.

### 3.3 Prescription immutability

A prescription attached to a past order is **never edited**. Changes create a new
version; the old one stays exactly as it was. Order lines keep their own
snapshot.

> *Rationale:* if a customer reports a problem with their glasses, the practice
> must be able to show precisely what was made and what it was made from. A
> record that can be quietly edited proves nothing.

### 3.4 Diopter entry

Diopter inputs are **dropdowns stepping 0.25 D**, drawn from a constants file. A
free-text field here becomes a lab remake.

Additionally enforced everywhere (customer form, staff screen, spreadsheet
import): a cylinder value **must** carry an axis.

### 3.5 Audit

Every mutation of a patient record, price or order writes an audit entry —
**but clinical values never appear in audit detail**.

### 3.6 Schema portability

The schema stays portable across SQL Server, Postgres and MySQL: no enums, no
arrays, no native JSON. Constrained strings are validated against a constants
file; JSON is a stringified column.

---

## 4. Data protection requirements

Patient and prescription data is health data and is treated as such.

Prescription and patient data must **never** appear in:

- URLs
- Logs
- Analytics payloads
- Error messages
- Audit metadata beyond what is necessary
- Client-side JavaScript unless required

Four roles, strictly separated: **Customer, Staff, Optician, Administrator**.
Staff must not reach administrator-only functionality. Customers must not reach
other customers' records or any staff page.

Customers must be able to obtain a copy of their data, request correction,
request restriction, and request erasure.

**Erasure is pseudonymisation, not deletion.** A prescription is a medical record
and an order is a financial record; both have retention obligations that outrank
a deletion request. Identity is destroyed; the records keep their shape.

---

## 5. Virtual try-on requirements

### 5.1 Privacy architecture — absolute

The customer's face is processed **entirely in the browser**. No camera frame,
and no photograph, is sent to the server unless the customer explicitly chooses
to save one.

The face model and its WebAssembly runtime are served **from the application's
own origin, not a CDN**, specifically so this stays true. The
Content-Security-Policy names no external origin.

### 5.2 Geometry

The overlay must account for:

- Face width, eye distance and eye position
- Head **roll, yaw and pitch**
- Frame width, height, aspect ratio, bridge position, lens centres
- Pupillary distance
- Camera distance and image dimensions

Placing a transparent frame between two detected pupils is insufficient. The
underlying geometry must be correct, not compensated for with per-frame scale
multipliers.

### 5.3 Behaviour

- The frame follows both eyes, scales with face distance, rotates with head tilt
- Tracking is **smoothed** to remove jitter without introducing lag
- Brief landmark loss is ridden out, not strobed through
- At extreme angles the system says so rather than drawing something visibly wrong
- Manual pupil placement remains available when detection fails
- Works with both uploaded photos and live camera

### 5.4 Honesty

PD is an **estimate**, and must be described as one. No claim of medical-grade
accuracy, "100% accurate", or "perfect fit guaranteed".

### 5.5 Frame calibration

Frame artwork carries anchor metadata. `DEFAULT_ANCHORS` and the artwork
generator are **two halves of one contract** — change one, change the other.

---

## 6. Functional requirements

### 6.1 Storefront

- Home with featured product
- Catalogue with filters: shape, material, rim type, wearer, brand, collection, size, price
- Frame detail with imagery, colourways, **measurements** (lens/bridge/temple in mm)
- Virtual try-on
- Lens builder: type, index/thickness, coatings, tints
- Prescription entry
- Cart with promotion codes
- Checkout with address, delivery method and payment selection
- Order confirmation and tracking

### 6.2 Customer account

- Registration, sign-in, password reset
- Order history
- Address book with a default address
- Appointment booking
- Data export and data-subject requests

### 6.3 Back office

Dashboard, orders, patients, prescriptions, clinic diary, frames, colourways,
lens options, media library, promotions, delivery rates, import/export, audit
trail, settings.

### 6.4 Clinic diary

- Slot booking with **no double-booking** for the same clinician
- Nothing bookable in the past or outside opening hours
- Cancellation frees the slot; completion does not
- Email confirmation on booking, move and cancellation

### 6.5 Import / export

- CSV for frames, patients, prescriptions and orders
- **Dry run first** — nothing written until confirmed
- Errors reported by spreadsheet line number
- A bad row does not roll back the good ones
- Money crosses the boundary in **major** units so the file round-trips
- Imported prescriptions always arrive pending verification

### 6.6 Email

Outbound mail is **queued, not sent inline**, so a slow SMTP server cannot stall
checkout and a failed confirmation is retried rather than lost. Drained inside
the worker process — no external worker, because shared hosting provides none.

---

## 7. Payment and shipping

New providers are **adapters**. Both must degrade safely when their key is
absent: a payment method with no key must not be offered at checkout.

Supported: cash on delivery, bank transfer, card (Stripe).

---

## 8. Design requirements

### 8.1 Direction

Premium but approachable optical retail. Trust, clarity, simplicity,
professional optical guidance.

**Avoid:** excessive gradients, glowing blobs, glassmorphism, floating cards,
excessive rounding, neon, generic illustrations, badge overuse, decorative
elements without purpose, and typography so large it wastes the screen.

Photography and real frame imagery are the visual focus.

### 8.2 Design system

A real system, not ad-hoc values:

- A **restrained type scale** — few sizes, clear hierarchy
- **Spacing tokens** — no invented margins per component
- Consistent radius; subtle shadows only where they signal elevation
- A restrained palette: neutral backgrounds, strong text, one brand colour, one
  accent, clear semantic states. Every colour has a purpose.
- Reusable components rather than duplicated markup

### 8.3 Content

Copy must read as though a real optical shop wrote it. Short, plain, human.

Explicitly banned: *"Discover the future of eyewear"*, *"seamless shopping
experience"*, *"revolutionise your eyewear journey"*, *"powered by cutting-edge
AI"*, *"unlock your perfect style"*, *"elevate your look"*, *"your journey starts
here"*.

Buttons describe their action — "Try it on", "Add to bag", "Choose lenses" —
not "Explore" or "Get started".

### 8.4 Responsive and accessible

Must work at 360, 390 and 430 px, tablet, laptop and desktop. Tables become
usable mobile layouts rather than overflowing.

Keyboard navigation, visible focus states, ARIA labels where needed, form
labels, sufficient contrast, image alt text, dialog and escape-key behaviour,
readable error messages.

### 8.5 States

Every important operation needs intentional loading, error and empty states.
Errors explain what the user can do next — never a bare "Something went wrong."

---

## 9. Quality requirements

### 9.1 Completion is not appearance

A feature is not complete because a page exists, a button exists, an API returns
200, a table exists, or a mock works.

Prohibited: TODO comments for core requirements, fake integrations, and
disabling functionality to make tests pass.

### 9.2 Testing

The geometry layer must be testable **without a browser or camera**. Business
rules must be covered by automated tests.

### 9.3 Code

Strict typing. No scattered magic numbers — named constants with a stated
reason, or frame-specific values in calibration data. Separate responsibilities:
detection, geometry, rendering, smoothing, measurement and UI state must not be
one component.

### 9.4 Secrets

**Never commit production secrets.** Infrastructure configuration comes from
environment variables. Published demo passwords must not be usable in
production.

---

## 10. Migration requirements

Recreate all legacy tables preserving primary keys, foreign keys, relationships,
unique constraints, indexes, soft-delete, prescription versioning and order
snapshots. Multi-table operations use transactions.

Before deleting or replacing anything: create a backup, and do not destroy the
original prematurely.

---

*Cross-references: implemented features are catalogued in
`02_FEATURES_DOCUMENT.md`; verified security controls in
`03_SECURITY_FEATURES_DOCUMENT.md`.*
