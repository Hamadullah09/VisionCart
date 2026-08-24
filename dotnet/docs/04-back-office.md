# VisionCart — Authentication, Email and the Back Office

**Phase 3–5 progress report.** Follows on from `03-tryon-studio.md`.

---

## 1. Authentication (prerequisite)

The back office needs sign-in, so that came first. ASP.NET Core Identity
replaces the legacy jose + bcryptjs pairing and brings three things the legacy
system did not have:

| Capability | Legacy | Now |
| --- | --- | --- |
| Password hashing | bcryptjs, 10 rounds | Identity PBKDF2 |
| Lockout after failed attempts | **none** — a password could be brute-forced indefinitely | 5 attempts, 15-minute lockout |
| Session revocation | none — a token stayed valid until it expired | Security stamp; a password reset signs out every existing session |
| Password recovery | **none** | Single-use tokens, six-hour expiry |

Sign-in, registration, forgotten password and reset are implemented, all four
rate-limited under the `auth` policy. Two enumeration defences carried over and
extended: the sign-in form returns the same message for "no such account" and
"wrong password", and the forgotten-password form returns the same confirmation
whether or not the address exists.

Staff signing in are routed to `/admin`; customers go where they were heading.

---

## 2. Email — the largest gap closed

The legacy application had a mail setting in its configuration and **not one
line of code that sent anything**. A customer who ordered received nothing.

Nine templates now exist: order confirmation, payment confirmation, order status
(lab / ready / delivered / cancelled / refunded), dispatch with tracking,
prescription verified, prescription rejected with the optician's reason, and
password reset.

**Mail is queued, never sent inline.** A slow or unreachable SMTP server must not
be able to stall checkout, and a failed order confirmation must be retried rather
than lost. `OutboxEmail` rows are drained by an `IHostedService` in the same
worker process — no external worker, which is what keeps this inside what shared
IIS hosting supports. Failures back off exponentially (1, 2, 4, 8 minutes) and
are abandoned after six attempts so one bad address cannot spin forever.

The default driver is `log`, which writes the message to the application log.
That makes a fresh install fully demonstrable without an SMTP account, and means
mail is never silently dropped.

> **No clinical values appear in any template.** An email is the least controlled
> place a prescription could end up.

---

## 3. The back office

### Screens delivered

| Screen | Status |
| --- | --- |
| Dashboard | ✅ 8 live figures, prescription queue, recent orders, low stock |
| Orders — list | ✅ filters on status, payment, lab stage; paged |
| Orders — detail | ✅ lab ticket, frozen Rx, payment, refund, dispatch |
| Patients — list | ✅ search, "pending prescription" filter, paged |
| Patients — file | ✅ details, versioned Rx history, verify/reject, documents, try-on snapshots, order history |
| Patients — new | ✅ |
| Frames — list | ✅ with try-on calibration status per frame |
| Frames — edit / new | ✅ including colourways **and try-on anchor calibration** |
| Lens options | ✅ all six groups, prices, Rx limits |
| Promotions — list / edit / new | ✅ |
| Settings | ✅ store details, selling rules, try-on switches, read-only integrations |
| **Audit log** | ✅ **new — did not exist** |
| **Delivery rates** | ✅ **new — did not exist** |
| Media library | ❌ **not migrated** |
| Import / export | ❌ **not migrated** |

14 of the legacy 16 screens, plus the 2 that were missing entirely. Two legacy
screens remain outstanding and are named in §6.

### The two screens the legacy application never had

**Audit viewer.** The trail was written in 26 places and read by nothing;
answering "who changed this price?" needed a database session. It now has search,
filters on action / entity / date, and pagination — administrators only, since it
spans every staff member's activity. A test asserts no entry carries a clinical
value.

**Delivery rates.** The `ShippingRate` table was read by the code and had no
screen, so changing a delivery price meant editing the database. The editor also
exposes the effective-date window added during migration, so a price change can
be scheduled rather than made by hand at the right moment.

### Authorization

The policy is applied on `AdminControllerBase`, so a new screen **cannot** be
added without it. The legacy application repeated a `staff()` call in twenty
places and relied on nobody forgetting.

Three tiers: staff reach the back office; **only opticians and administrators may
verify or reject a prescription**; only administrators reach the audit log.

---

## 4. Business rules verified by test

| Rule | Why it matters |
| --- | --- |
| Lenses cannot be marked **ready** while the prescription is unverified | Without it, an unchecked prescription reaches the lab and a customer gets lenses nobody qualified ever looked at |
| Intermediate lab stages *are* allowed before verification | Surfacing and coating happen on the blank; over-gating would stall the lab |
| After an optician verifies, ready is permitted | The gate opens, not just closes |
| Cancelling an order **returns the frames to stock** | Otherwise every cancellation silently loses a frame and the shop drifts into overselling |
| Manual payment uses the **same transition as the webhook** | The two can never diverge |
| Recording the same manual payment twice is refused | Cannot double-count revenue |
| A new prescription is a **new version**; the original is untouched | The order dispensed against the original still reads correctly |
| `-2.13` is refused at the **optician's** screen too | The rule that guards the customer form must guard the staff form |
| Verify and reject both **queue the customer an email** | The notification gap |
| Audit entries are readable and carry **no clinical values** | The log is read far more widely than the records it describes |
| Try-on calibration refuses anchors outside the image or too close together | Either would misplace every frame |
| `1499.50` typed by staff becomes exactly `149950` minor units | Money converts once, at the edge |
| A delivery rate with max ETA below min is refused | |

---

## 5. Verified live

Signed in as the seeded administrator:

- All 12 admin index routes return **302 to sign-in without a session**, 200 with one.
- Sign-in redirects staff to `/admin`.
- Dashboard reports real aggregates from the database (62 orders, 57 patient
  files, 11 prescriptions to check, 19 low-stock lines at time of testing).
- Order, patient, frame and promotion detail pages all render.
- All four auth pages render.

**Test suite: 108 tests, all passing** — 26 unit, 53 integration against real SQL
Server, 29 try-on geometry. Clean build, 0 warnings.

---

## 6. Known limitations

- **Media library not migrated.** `LocalStorageProvider` exists and is used by
  the try-on snapshot route, so the processing pipeline is done; the bulk
  drag-and-drop upload screen and the searchable library are not built. Frame
  images can only be attached by URL on the colourway form.
- **Import / export not migrated.** The four CSV exports and two imports with
  dry-run validation are outstanding. `ImportJob` and its `IsDryRun` column exist.
- **Cloud media deletion still unfixed.** The `MediaAsset` purge-tracking columns
  are in place but nothing sweeps them yet — this belongs with the media library.
- **Customer address book not built.** `Address.DeletedAt` exists for it.
- **Appointments not built.** The schema, including the staff and reminder
  columns added during migration, is ready.
- **The refund path is untested against a live Stripe key** — none available
  here. The code path is real and the COD/bank-transfer paths are tested.
- **Emails are queued and drained, but never actually sent over SMTP in this
  environment.** The `log` driver is exercised; the SMTP sender is not.
