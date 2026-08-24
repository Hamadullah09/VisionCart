# VisionCart — Media Library and Import/Export

**Phase 5 completion report.** Follows on from `04-back-office.md`.

These are the last two of the eighteen back-office screens. With them the back
office is feature-complete against the legacy application.

---

## 1. Media library (`/admin/media`)

Bulk upload for product photography and try-on overlays.

### Upload pipeline

`sharp` is not available on the target stack, so the pipeline is rebuilt on
**SkiaSharp** (MIT — see `01-migration-inventory.md` §7 for why ImageSharp was
rejected). Behaviour is preserved:

| Step | Behaviour |
| --- | --- |
| EXIF orientation | Auto-rotated, so phone photos are not sideways |
| Maximum edge | Capped at 2000 px |
| Format | Converted to WebP |
| Thumbnail | Generated alongside the full image |
| Transparency | **Kept as PNG** when *keep transparency* is ticked — a WebP-flattened try-on overlay would render with a white box around the frame |

Files post **one at a time**, not as a single multipart batch. This is carried
over from the legacy uploader for two reasons: per-file progress is meaningful,
and one corrupt image is reported by name while the rest of the shoot still
goes through.

### Deletion is two-phase

`MediaService` sets `DeletedAt`, deletes the storage object, then sets
`PurgedAt`. If the storage delete throws, the row stays half-deleted and
`MediaPurgeService` — an `IHostedService` sweeping hourly — retries it, up to
`MaxPurgeAttempts = 5`.

The reason is that the database and the file system cannot be committed
together. Deleting the row first risks orphaned files that nothing can ever
find; deleting the file first risks a library entry pointing at nothing. The
two-phase record makes the in-between state explicit and recoverable.

An image **attached to a colourway cannot be deleted** — the service refuses
rather than leaving a product page with a broken photo.

### Try-on artwork

Attaching an image with the `TryOn` role sets the variant's `TryOnImageUrl`,
which is what makes a colourway appear in the mirror. This is the only way a
frame enters the try-on studio.

---

## 2. Import and export (`/admin/import`)

### CSV parsing

`Csv.cs` is a direct port of the legacy `csv.ts`, and deliberately not a
`string.Split(',')`. Eleven unit tests cover the shapes a real spreadsheet
export actually contains:

- quoted fields containing commas
- doubled quotes as a literal quote
- quoted fields spanning multiple lines
- CRLF line endings
- a UTF-8 BOM (Excel writes one; leaving it in makes the first column
  unfindable by name)
- no trailing newline
- blank lines
- headers trimmed and lower-cased

Export writes a BOM so Excel opens the file as UTF-8 rather than mangling
non-ASCII names.

### Money crosses the boundary in major units

Internally money is always an integer of minor units. The export emits
**major** units (`8500`, not `850000`) because that is what a human edits, and
the importer converts once at the edge with `toMinor`. This keeps the file
round-trippable: export it, edit it in a spreadsheet, import it back.

### Matching and safety

| Dataset | Matched on |
| --- | --- |
| Frames | `variant_sku` |
| Patients | `file_no` (blank creates a new file) |

Three protections carried over:

1. **The check runs first.** A dry run reports what would happen and writes
   nothing. Verified by a test that asserts the row is absent afterwards.
2. **Errors are reported by spreadsheet line number** (`line = i + 2`,
   accounting for the header row and 1-based numbering), so a staff member can
   find the bad row in Excel.
3. **A bad row does not roll back the good ones.** Verified live: a two-row
   file with one invalid row imported the valid row and reported
   `1 ok · 1 failed — line 3`.

Clinical validation is identical to the customer and optician forms: diopters
must sit on a 0.25 D step, and a cylinder must carry its axis. An imported
prescription always arrives **pending verification** — it never goes straight
to the lab.

Patient and prescription **exports are audited** (`AuditActions.ExportPatients`).
A file of clinical data leaving the building is an event worth recording.
Per the brief's §10, the audit detail records that an export happened, not what
was in it.

---

## 3. Two defects found and fixed

### 3.1 The integration suite drained its own seed data

Every order a test placed decremented real stock, and nothing put it back.
Tests looked for a variant with `StockQty > 1`; after 167 accumulated orders
every active colourway sat at ≤ 1 unit and **23 of 80 tests failed at once**,
with a misleading `Sequence contains no elements`.

The tests were not wrong about the invariant, they were wrong about ownership:
stock a test depends on is part of its *arrange* step. `CheckoutFlowFixture`
now exposes `SellableVariantIdAsync(minStock)`, which tops the shelf up before
returning the id. Seven call sites use it. The suite now passes repeatedly on
an already-drained database.

### 3.2 The media uploader's antiforgery token was always empty

The view passed the token to JavaScript as
`data-token="@Html.AntiForgeryToken().ToString()"`. That compiles and renders,
but `IHtmlContent.ToString()` returns the *type name* — the attribute held the
literal string `Microsoft.AspNetCore.Mvc.ViewFeatures.AntiforgeryExtensions+InputContent`.
The token was empty, and **every upload from a browser was rejected with a 400**.

No test caught it. The integration tests call `MediaService` directly and never
cross the HTTP boundary, so the upload pipeline was green while the feature was
entirely broken in the browser. It was found only by driving a real upload
through the page.

The token is now rendered as a real hidden field and read from the DOM.

`ViewConventionTests` guards the class of defect: it scans every `.cshtml` for
a stringified antiforgery token or one embedded in an HTML attribute. It is a
lint, not a behavioural test, and is documented as such — it exists because the
failure mode is silent and a reviewer cannot tell the difference by eye. The
guard was verified by reintroducing the defect and confirming it fails.

---

## 4. Live verification

Signed in as the seeded administrator on `localhost`, against a real SQL Server
LocalDB database. Not "the page loads" — the actual work:

| Check | Evidence |
| --- | --- |
| Upload | 900×600 PNG posted through the page's own uploader → stored as `browser-check-eaaa65383c.webp`, 5182 bytes |
| Conversion | PNG in, WebP out; thumbnail generated at 2068 bytes |
| Serving | Both assets return `HTTP 200 image/webp` |
| Delete | Library returns to empty; `DeletedAt` and `PurgedAt` both set; both files gone from disk |
| Export | `HTTP 200 text/csv`, `Content-Disposition: attachment; filename=frames-2026-08-24.csv`, 30 rows |
| Import (dry run) | Reports without writing |
| Import (real) | Stock 2 → 7 on `VC-ATLA-BLA`, confirmed by re-exporting |
| Partial failure | `2 rows read · 1 ok · 1 failed`, bad row reported at line 3 |

---

## 5. Test totals

| Project | Tests |
| --- | --- |
| `VisionCart.UnitTests` | 73 |
| `VisionCart.IntegrationTests` | 80 |
| **Total** | **153**, all passing (since grown to 184 — see `06-http-harness.md`) |

---

## 6. Known gap — now closed

**Nothing in the suite crossed the HTTP boundary.** Both defects above were
invisible to 153 passing tests, and the antiforgery bug was found by hand.

This has since been addressed: see `06-http-harness.md`. The harness found a
third defect on its first run.

---

## 7. Remaining work

Unchanged from `04-back-office.md`, less the two screens delivered here:

- Customer address book
- Appointments module
- Data-subject correction and erasure flow
- IIS deployment validation and the deployment manual
- Final audit sweep
