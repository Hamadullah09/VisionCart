# VisionCart — Migration Inventory and Architecture Mapping

**Phase 1 (Discovery) and Phase 2 (Architecture) deliverables.**
Assessed 24 August 2026 against the running Next.js application and all 77 source files.

---

## 1. Host environment — verified, not assumed

Section 4 of the brief forbids guessing the hosting environment. These were
checked on this machine before any code was written:

| Capability | Found | Consequence |
| --- | --- | --- |
| .NET SDK | **10.0.400** | ASP.NET Core 10 target confirmed available |
| ASP.NET Core runtime | **10.0.11** | Framework-dependent deployment viable |
| SQL Server | **LocalDB 2019 (15.0.4382.1) Express** | Matches a myASP.NET-supported version (2019); migrations and integration tests run against a real engine, not an in-memory fake |
| Node.js / npm | 24.15.0 / 11.12.1 | Build-time only, for the Tailwind and TypeScript bundle. **Not required at runtime.** |
| EF Core tooling | dotnet-ef 10.0.11 | Code-first migrations confirmed working |

> **Operational note.** The default `MSSQLLocalDB` instance on this machine lost the
> `DataDirectory` value from its registry key mid-session and could no longer start.
> A replacement instance `VisionCartDev` was created and the schema rebuilt on it.
> This affects local development only; it has no bearing on the production target.

---

## 2. Dependency inventory and disposition

Every runtime and build-time dependency of the legacy application, and what
happens to it.

| Dependency | Role | Disposition |
| --- | --- | --- |
| next 16.3.1 | Framework, routing, SSR, server actions | **Replaced** — ASP.NET Core 10 MVC |
| react 19.2.8 / react-dom | UI rendering | **Replaced** — Razor views + TypeScript modules |
| typescript 5.x | Types | **Split** — C# server-side, TypeScript retained for the browser bundle |
| tailwindcss 4.x | Styling | **Retained**, compiled to static CSS at build time; no dev server at runtime |
| prisma / @prisma/client 6.19.3 | ORM, migrations | **Replaced** — EF Core 10 |
| SQLite | Database | **Replaced** — SQL Server |
| @mediapipe/tasks-vision 1.0.1 | In-browser face landmarks | **Retained unchanged** — this is client-side and must stay so |
| sharp 0.35.3 | Server image processing | **Replaced** — see §3, licensing |
| jose 6.2.9 | JWT session signing | **Replaced** — ASP.NET Core cookie authentication |
| bcryptjs 3.0.3 | Password hashing | **Replaced** — ASP.NET Core Identity (PBKDF2, 100k iterations) |
| zod 4.4.3 | Schema validation | **Replaced** — FluentValidation + data annotations |
| stripe 22.5.0 | Card payments | **Replaced** — Stripe.net 52.3.0 |
| eslint 9 | Linting | **Replaced** — .NET analyzers; ESLint retained for the browser bundle |
| tsx 4.23.12 | Seed script runner | **Replaced** — C# seeder invoked from the host |

### Licensing findings (not in the original assessment)

Two packages that look like the obvious choice carry commercial licences. Both
were caught during this migration and avoided:

| Package | Issue | Decision |
| --- | --- | --- |
| **SixLabors.ImageSharp** ≥ 3.0 | Six Labors Split License — **paid** for commercial use. Only the 1.x/2.x line is Apache-2.0, and that line is end-of-life, which makes it a poor choice for parsing untrusted uploads. | **Rejected.** Using **SkiaSharp 4.151.1** (MIT, Microsoft-maintained) behind an `IImageProcessor` abstraction so the decision stays reversible if a licence is later purchased. |
| **FluentAssertions** ≥ 8.0 | Moved to a paid Xceed licence; v7 was the last free release. | **Rejected.** xUnit's built-in assertions carry no licensing risk. |

> These are genuine budget items. If a commercial ImageSharp licence is preferred
> for its image-quality and format coverage, only one class behind
> `IImageProcessor` changes.

### Node.js-specific features found

| Feature | Location | Replacement |
| --- | --- | --- |
| `node:fs/promises`, `node:path` | `src/lib/storage.ts` | `System.IO` via `IStorageProvider` |
| `node:crypto` randomBytes | `src/lib/cart.ts`, `storage.ts` | `RandomNumberGenerator` |
| `process.cwd()` path joins | `storage.ts:138,195` | `IWebHostEnvironment.WebRootPath` — and this is a **path-traversal fix**, see §6 |
| `fetch()` to carrier APIs | `shipping.ts:149,204,243` | `IHttpClientFactory` with typed clients |

### Next.js-specific features found

| Feature | Count | Replacement |
| --- | --- | --- |
| `"use server"` files | 4 (32 actions) | MVC controller actions |
| `"use client"` components | 11 | Razor views + TypeScript modules |
| `next/link` | 29 | Razor anchor tag helpers |
| `next/navigation` (`redirect`, `notFound`) | 18 | `RedirectToAction`, `NotFound()` |
| `revalidatePath` | 36 | `IMemoryCache` eviction / response cache tags |
| `cookies()` / `headers()` | 7 | `HttpContext.Request` |
| `next/font/google` | 1 | Self-hosted webfont — **also a privacy improvement**, see §6 |

---

## 3. Technology mapping

| Legacy | Target | Rationale |
| --- | --- | --- |
| Next.js App Router | ASP.NET Core 10 MVC | No permanently-running Node process; IIS hosts the app natively |
| React server components | Razor views | Server-rendered HTML, same rendering model |
| React client components | TypeScript modules bundled to static JS | Only the genuinely interactive parts ship code — same split the legacy app made |
| Server actions | Controller actions with antiforgery tokens | Direct equivalent; CSRF protection becomes explicit rather than framework-implicit |
| API routes | Controllers / minimal API endpoints | Multipart upload, CSV, webhooks |
| Prisma | EF Core 10 | Mandated |
| SQLite | SQL Server | Mandated |
| jose JWT in a cookie | ASP.NET Core cookie authentication | Removes hand-rolled token handling |
| bcryptjs | Identity `PasswordHasher` | Also brings lockout and single-use reset tokens |
| Zod | FluentValidation | Server-side, same field-level error shape |
| Prisma migrations | EF Core migrations | Code-first, applied at deploy |
| `tsx prisma/seed.ts` | `DatabaseSeeder` | Idempotent, invoked from the host |
| npm scripts | dotnet CLI + MSBuild targets | Tailwind/esbuild run as a build step |

### Frontend approach chosen

**ASP.NET Core MVC + Razor + compiled TypeScript.** Selected over Razor Pages or a
separate SPA because the brief asks to *minimise operational complexity on shared
Windows/IIS hosting*, and because the legacy application already renders almost
everything on the server — only 11 of 77 files were client components. A SPA
would mean rebuilding pages that are currently plain server-rendered HTML, for no
benefit, and would need a second deployable artifact.

---

## 4. Database migration — status: **complete and verified**

The full schema is migrated, applied to SQL Server, and covered by tests.

### Verified schema statistics

Queried directly from `sys.tables` / `sys.indexes` / `sys.foreign_keys` after
applying the migration:

| Measure | Count |
| --- | --- |
| Tables | 36 |
| Columns | 429 |
| Primary keys | 36 |
| Foreign keys | 38 |
| Non-PK indexes | 97 |
| Unique indexes | 15 |
| Filtered indexes | 6 |
| FKs with `ON DELETE CASCADE` | 17 |
| FKs with `ON DELETE SET NULL` | 12 |
| FKs with `NO ACTION` | 9 |

36 tables = **26** carried over from Prisma (the 27th, `User`, becomes
`AspNetUsers`) + **7** ASP.NET Core Identity tables + **2** added during migration
+ `__EFMigrationsHistory`.

### The single biggest migration hazard: SQL Server cascade paths

SQL Server rejects any schema where two cascading actions can reach the same table
along different paths. **Seven Prisma relationships violated this.** Each had to be
demoted, and in every case the demotion turned out to *strengthen* an invariant the
legacy system enforced only by convention.

| Relationship | Prisma | Now | Why |
| --- | --- | --- | --- |
| `Order → ShippingAddress` | SetNull | NoAction | `Order` reaches `Address` twice (shipping + billing); with `Address → User` cascading, three cascade paths converge on `Order` |
| `Order → BillingAddress` | SetNull | NoAction | as above |
| `Address → User` | Cascade | NoAction | same convergence; addresses on past orders are delivery evidence and must outlive the account |
| `OrderItem → Prescription` | SetNull | **Restrict** | **Strengthening.** The database now refuses to delete a prescription an order was dispensed against — "prescriptions are immutable once used" becomes a constraint, not a convention |
| `OrderItem → FrameVariant` | SetNull | **Restrict** | protects order history from catalogue deletion |
| `CartItem → FrameVariant` | Cascade | NoAction | second cascade path into `CartItem` alongside `Cart` |
| `TryOnSnapshot → FrameVariant` | Cascade | NoAction | second cascade path alongside `TryOnSession` |
| `Category → Category` (parent) | SetNull | NoAction | SQL Server forbids **any** cascading action on a self-referencing FK |

> **`Restrict`, not `NoAction`, on the two `OrderItem` relationships — and the
> difference matters.** Both emit an identical restrictive foreign key. But
> `NoAction` leaves EF Core's client-side fixup in place: with the order line
> tracked in memory, EF nulls `PrescriptionId` and issues an `UPDATE` *before* the
> `DELETE`, severing the clinical link by the back door and never touching the
> constraint. This was caught by an integration test that expected a failure and
> got a success. `Restrict` disables that fixup.

### Other schema issues found and resolved

| Issue | Resolution |
| --- | --- |
| **Identity key length mismatch.** `ApplicationUser.Id` narrowed to 30 chars for cuid keys, but Identity's child tables still declared `nvarchar(450)`. SQL Server error 1753 — FK columns must match exactly. | All five Identity child tables narrowed in step. Also shrinks five indexes considerably. |
| **`@unique` on a nullable column.** Prisma allowed unlimited NULLs on `Promotion.Code`. A plain SQL Server unique index allows exactly **one** — which would have silently capped the shop at a single automatic promotion. | Filtered unique index `WHERE [Code] IS NOT NULL`. Covered by a behavioural test that inserts two code-free promotions. |
| **Unbounded strings.** Prisma emitted `TEXT` for every string; SQL Server cannot index `nvarchar(max)`. | Explicit lengths on every indexed column; long free-text and JSON columns opt into `nvarchar(max)` deliberately. A test asserts no indexed column is `nvarchar(max)`. |
| **No `@updatedAt` equivalent.** Prisma stamped the column automatically; EF Core has no equivalent, so `UpdatedAt` would have frozen at creation. | `TimestampInterceptor` applies it by convention on every insert and update. |
| **Random primary keys fragment clustered indexes.** | `Cuid` generator retained (rather than switching to `Guid`), keeping keys time-ordered *and* letting an existing production dataset migrate with its identifiers intact. |

### Money invariant — enforced at three levels

The legacy rule "money is always an integer of minor units" is now guarded by:

1. `Money` value object — the only conversion point, no floating-point arithmetic.
2. A model-building guard in `ApplicationDbContext` that **throws at startup** if
   any `*Minor` property is not an `int`.
3. An integration test asserting all **24** money columns across 10 tables are
   `int` in the live database.

### Tables added during migration

| Table | Purpose |
| --- | --- |
| `OutboxEmail` | Queues outbound mail so a slow SMTP server cannot stall checkout and a failed order confirmation is retried rather than lost. Drained by a hosted service in-process — **no external worker**, which keeps deployment inside what shared IIS hosting supports. |
| `DataSubjectRequest` | Serves the correction/erasure obligation the legacy schema had consent and retention *fields* for but no workflow. |

Plus columns on existing tables: `Payment.IdempotencyKey` (unique, filtered —
blocks webhook replay), `MediaAsset.StorageKey`/`PurgedAt`/`PurgeError` (fixes
cloud orphaning), `Appointment.StaffUserId`, `Address.DeletedAt`,
`AuditLog.ActorEmail`/`UserAgent`, `Frame.SearchText`, `ShippingRate.EffectiveFrom`/`To`.

---

## 5. Verification evidence

`dotnet test tests/VisionCart.IntegrationTests` — **9 passed, 0 failed**, against
real SQL Server:

| Test | Proves |
| --- | --- |
| `All_27_legacy_tables_plus_migration_additions_exist` | No table lost in translation |
| `Money_columns_are_all_integers` | All 24 money columns are `int` |
| `Unique_constraints_from_the_prisma_schema_survive` | 10 load-bearing unique constraints intact |
| `Nullable_promo_code_allows_many_automatic_promotions` | Filtered index reproduces Prisma NULL semantics |
| `Prescription_used_by_an_order_cannot_be_deleted` | Database rejects the delete (raw SQL, so it holds for maintenance scripts too) |
| `Order_line_relationships_disable_client_side_fixup` | `Restrict`, so EF cannot sever the link in memory |
| `No_indexed_column_is_nvarchar_max` | Every index is actually creatable |
| `Payment_webhook_replay_is_blocked_by_the_database` | Duplicate provider event rejected by unique constraint |
| `Schema_applies_from_empty` | Migration runs clean on a fresh database |

### Unit tests — `dotnet test tests/VisionCart.UnitTests` — **26 passed, 0 failed**

Money arithmetic (integer minor units, half-up rounding, culture-independent
formatting, no drift over 1,000 repeated percentage discounts) and the `Cuid`
identifier generator (shape, uniqueness under 20,000 concurrent mints,
time-ordering so clustered indexes do not fragment).

One test reproduces the exact figure observed in the running legacy application:
a Rs.6,500 frame with `WELCOME15` and Rs.300 delivery totals Rs.5,825.

### Try-on geometry — `node --test geometry.test.ts` — **29 passed, 0 failed**

Run against the **browser** implementation, which is byte-identical to the legacy
`src/lib/tryon.ts` (verified with `diff`). A .NET port would be tested code that
never executes in production, since the maths runs only in the customer's browser.
Node 24 strips types natively, so this needs no test framework and no build step.

Covered: the anchors land on the pupils to within 1e-9 under the reproduced canvas
transform; scale is the pupil/anchor span ratio; head tilt maps to frame rotation;
customer nudges compose on top of the auto-fit; PD uses the 11.7 mm iris ruler and
is resolution-independent; confidence collapses for a turned head and is capped at
0.2 for an anatomically implausible PD; detection degrades to pupils-only without
iris points rather than guessing; and `DEFAULT_ANCHORS` still matches the contract
with `scripts/generate-frame-assets.mjs`.

**Total: 64 tests, all passing.**

---

## 6. Security and privacy issues found in the legacy code

Discovered during discovery, beyond the documented gap list:

| Finding | Severity | Note |
| --- | --- | --- |
| `storage.ts:195` joins a user-influenced `url` into a filesystem path with no traversal guard before `fs.rm` | **High** | Being fixed in `IStorageProvider` with canonical-path containment checks |
| `next/font/google` fetches a webfont from Google on page load | Medium | Contradicts the project's own privacy stance ("no third party ever sees a customer"). Self-hosting the font in the migration. |
| Session token had no server-side revocation | Medium | Identity's security stamp gives revocation on password change |
| No lockout on repeated failed logins | Medium | Identity lockout enabled |

---

## 7. Feature parity matrix

Legend: **Done** = migrated and verified · **Foundation** = schema and domain model
in place, service and UI outstanding · **Pending** = not started.

| Feature | Status |
| --- | --- |
| Database schema (27 tables) | **Done** — applied to SQL Server, 9 tests green |
| Money / minor-unit invariant | **Done** — enforced at 3 levels |
| Prescription versioning + immutability | **Done** — now database-enforced |
| Order snapshot columns | **Done** |
| Constants / constrained strings | **Done** — ported in full |
| Cuid identifiers | **Done** |
| Identity, roles, password hashing | **Foundation** — model and tables done; flows outstanding |
| Homepage, catalogue, search, filters | Foundation |
| Product detail, frame variants, stock | Foundation |
| Try-on studio, PD, calibration | Pending — client assets carry over unchanged |
| Lens builder | Foundation |
| Cart, promotions, checkout, guest checkout | Foundation |
| Payments (COD, bank transfer, Stripe + webhook) | Foundation — idempotency column done |
| Shipping (rate table + 2 carriers + fallback) | Foundation |
| Patient records, Rx verification, lab workflow | Foundation |
| Media, import/export, audit trail | Foundation |
| Email notifications | Foundation — `OutboxEmail` table done |
| Password recovery | Foundation — Identity tokens available |
| Rate limiting | Pending |
| Four missing guide pages | Pending |
| Custom error pages | Pending |
| Audit log viewer | Foundation — indexes added |
| Delivery-rate editor | Foundation — effective-date columns added |
| Customer address book | Foundation — soft-delete column added |
| Appointments | Foundation — staff/reminder columns added |
| Cloud media deletion fix | Foundation — purge-tracking columns added |

---

## 8. Hosting compatibility matrix

| Requirement | Status |
| --- | --- |
| ASP.NET Core 10 on IIS | ✅ Runtime 10.0.11 present; framework-dependent publish |
| SQL Server | ✅ Verified against SQL Server 2019 |
| No Docker / Kubernetes / Linux | ✅ None used |
| No permanently-running Node server | ✅ Node is build-time only |
| No PostgreSQL | ✅ Not referenced |
| No Prisma | ✅ Removed |
| No SQLite in production | ✅ SQL Server only |
| No native dependencies needing install rights | ⚠️ SkiaSharp ships a native `libSkiaSharp.dll` in the publish output. It is xcopy-deployable and needs no installer, but **must be confirmed on the target host** before go-live. |
| Background work without an external worker | ✅ Email outbox drains via `IHostedService` in-process |
