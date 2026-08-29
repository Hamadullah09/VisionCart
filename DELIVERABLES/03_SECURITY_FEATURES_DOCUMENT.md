# VisionCart — Security Review

**Prepared** 25 August 2026
**Scope** Full source inspection of `dotnet/` plus live verification against a running instance
**Method** Code reading, configuration inspection, and runtime checks. Every control below was confirmed in source; nothing is asserted from documentation alone.

**Status vocabulary**

| Marker | Meaning |
| --- | --- |
| **Implemented** | Verified present and working |
| **Partial** | Present but incomplete or unverified in part |
| **Not found** | Searched for; not present |
| **Recommended** | Suggested improvement, not currently required |

---

## 1. Executive summary

The application's security posture is **good for its class**. Authentication,
authorisation, CSRF, transport security, response headers and rate limiting are
all implemented and verified. Two findings emerged during this review; one was
fixed during it.

| Severity | Count | Detail |
| --- | --- | --- |
| High | 0 | — |
| Medium | 0 | — |
| Low | 1 | Stored XSS via promotion code — **fixed during this review** (§9.1) |
| Informational | 3 | §9.2 – §9.4 |

---

## 2. Security architecture

```
Browser
  │  HTTPS (HSTS in production)
  ▼
IIS  ──  web.config: request limits, hidden segments,
  │       blocked extensions, Server header removed
  ▼
ASP.NET Core pipeline
  │  1. Forwarded headers      (real client IP)
  │  2. Exception handler      (no stack traces to customers)
  │  3. HSTS + HTTPS redirect
  │  4. Response compression
  │  5. Security headers       (CSP, nosniff, frame options, referrer, permissions)
  │  6. Static files           (explicit content types)
  │  7. Routing
  │  8. Rate limiter           (per client, per policy)
  │  9. Authentication         (cookie)
  │ 10. Authorisation          (role policies)
  ▼
Controllers  ──  global antiforgery filter on every POST
  ▼
Application services  ──  ownership scoping, validation, audit
  ▼
EF Core  ──  parameterised throughout; no raw SQL in application code
  ▼
SQL Server
```

---

## 3. Authentication

**Status: Implemented**

ASP.NET Core Identity 10.0.11 with `ApplicationUser : IdentityUser<string>`.

| Control | Value | Verified in |
| --- | --- | --- |
| Password hashing | Identity default (PBKDF2, HMAC-SHA256, 100k iterations) | Identity defaults |
| Minimum length | 8 | `DependencyInjection.cs` |
| Requires digit | Yes | `DependencyInjection.cs` |
| Requires lowercase | Yes | `DependencyInjection.cs` |
| Requires uppercase | No | `DependencyInjection.cs` |
| Requires non-alphanumeric | No | `DependencyInjection.cs` |
| Unique email required | Yes | `DependencyInjection.cs` |
| Lockout after failures | 5 attempts | `DependencyInjection.cs` |
| Lockout duration | 15 minutes | `DependencyInjection.cs` |
| Session cookie | `vc_session`, 30 days sliding | `Program.cs` |
| Security stamp | Rotated on password change and on erasure | Identity + `DataSubjectService` |

### 3.1 Password recovery

Single-use tokens with a six-hour expiry. A reset **signs out every existing
session** via the security stamp.

### 3.2 User enumeration defences

**Status: Implemented — verified by test**

Sign-in returns the same message for "no such account" and "wrong password".
Forgotten-password returns the same confirmation whether or not the address
exists.

`HttpRateLimitTests.A_failed_sign_in_does_not_reveal_whether_the_account_exists`
compares the rendered validation message between a known and an unknown account
and asserts they are identical.

> **Note on rigour.** The test deliberately compares the *message shown to the
> visitor*, not the whole page. The form legitimately echoes back the typed
> address and the antiforgery token rotates per request; neither tells an
> attacker anything, and comparing raw HTML would fail for reasons unrelated to
> enumeration.

---

## 4. Authorisation

**Status: Implemented**

Four roles, three policies:

| Policy | Grants | Applied to |
| --- | --- | --- |
| `StaffOnly` | Staff, Optician, Admin | `AdminControllerBase` — every back-office controller |
| `OpticianOnly` | Optician, Admin | Prescription verification |
| `AdminOnly` | Admin | Audit trail, settings, data erasure |

Customer-owned resources are scoped by owner **in the service layer**, not by id
alone:

- `AddressService` — every read filters on `UserId`
- `AppointmentsController.Cancel` — resolves the caller's own patient file first
- `CartService` — cart items matched against the caller's cart id

### 4.1 Verified by test

| Assertion | Test |
| --- | --- |
| Anonymous is redirected to `/login` from six back-office routes | `HttpAuthorizationTests` |
| A signed-in customer is refused with `/error/403` | `HttpAuthorizationTests` |
| Staff are refused the audit trail; an administrator is admitted | `HttpAuthorizationTests` |
| Clinical exports are unreachable anonymously and do not return `text/csv` | `HttpAuthorizationTests` |
| One customer cannot read, edit or delete another's address | `AddressBookTests` |
| An export contains the customer's own records and nobody else's | `PrivacyTests` |

---

## 5. API and request security

### 5.1 CSRF

**Status: Implemented**

`AutoValidateAntiforgeryTokenAttribute` is registered as a **global filter**, so a
new POST cannot be added without protection. Verified by test: a POST without a
token returns **400**.

> **Finding history.** The media uploader previously passed its token via
> `data-token="@Html.AntiForgeryToken().ToString()"`. `IHtmlContent.ToString()`
> returns the type name, not the markup, so the token was empty and every upload
> was rejected with a 400 — a silent, total failure of that feature.
> `ViewConventionTests` now lints every `.cshtml` for stringified or
> attribute-embedded antiforgery tokens, and the guard was verified by
> reintroducing the defect and confirming it fails.

### 5.2 Rate limiting

**Status: Implemented**

| Policy | Limit | Window | Applied to |
| --- | --- | --- | --- |
| `auth` | 8 | 5 min | Sign-in, registration, password reset, data requests |
| `checkout` | 20 | 5 min | Checkout, appointment booking |
| `upload` | 30 | 5 min | Media upload, CSV import, data download |

**Partitioned per client** — keyed on the authenticated account when present,
falling back to the connecting IP.

> **Finding history.** These were originally created with
> `AddFixedWindowLimiter`, which gives the **whole policy one bucket shared by
> every visitor**. Eight bad passwords from any one person would have locked
> every other customer out of signing in for five minutes — a denial-of-service
> vector rather than a brute-force defence. Found by the HTTP test harness and
> fixed; `One_visitor_cannot_throttle_everybody_else` guards it, verified by
> reinstating the defect and confirming the test fails.

### 5.3 CORS

**Status: Not found — and correct**

No CORS policy is configured. The application is server-rendered and exposes no
cross-origin API, so the browser same-origin policy is the intended boundary.
Adding a permissive policy would only widen the attack surface.

### 5.4 Response headers

**Status: Implemented** — verified live on every response

| Header | Value |
| --- | --- |
| `Content-Security-Policy` | see below |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | `camera=(self), microphone=(), geolocation=()` |
| `Server` | removed (Kestrel `AddServerHeader = false`; IIS strips its own) |
| `X-Powered-By` | removed via `web.config` |

```
default-src 'self'; script-src 'self' 'wasm-unsafe-eval';
style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:;
media-src 'self' blob:; connect-src 'self'; frame-ancestors 'none';
base-uri 'self'; form-action 'self'
```

**No external origin appears anywhere in the policy.** This is load-bearing for
the try-on privacy claim: it is what blocks MediaPipe's own telemetry call to
`odml.pa.googleapis.com`, observed being refused in the browser console.

`'unsafe-inline'` is present for **styles only**, not scripts. See §9.2.

---

## 6. Data security

### 6.1 Face and camera data

**Status: Implemented**

All landmark detection, pose estimation and rendering happen in the browser. The
model and WebAssembly runtime are served from the application's own origin
precisely so no third party observes a customer's face. A photograph reaches the
server only on an explicit *Save to my file*.

### 6.2 Clinical data handling

**Status: Implemented**

| Rule | Enforcement |
| --- | --- |
| No clinical values in audit detail | Audit calls pass ids and categories only; verified by `BackOfficeTests` and `PrivacyTests` |
| No patient data in URLs | Routes use opaque `Cuid` identifiers; verified by `HttpPrivacyTests` |
| No stack traces to customers | `UseExceptionHandler` + `UseStatusCodePagesWithReExecute`; verified by `HttpPrivacyTests` |
| Clinical export audited | `AuditActions.ExportPatients` on both staff and self-service export |

`PrivacyTests.An_audit_entry_records_the_kind_but_never_what_the_customer_wrote`
stores a message containing a prescription value and asserts it does not appear
in the audit trail.

### 6.3 Erasure

**Status: Implemented**

Pseudonymisation in a single transaction, administrator-only, requiring the typed
word `ERASE`, blocked while an order is in flight. Rotates the security stamp so
existing sessions die. Verified by `PrivacyTests` — including that prescriptions
and order totals survive.

---

## 7. Database security

### 7.1 SQL injection

**Status: Implemented (by construction)**

**Zero** occurrences of `FromSqlRaw`, `ExecuteSqlRaw` or `FromSqlInterpolated` in
application code. All access is through EF Core LINQ, which parameterises.

Five raw-SQL occurrences exist in **tests only**, deliberately — one asserts that
a foreign key rejects a delete at the database level, which cannot be shown
through EF's client-side fixup.

### 7.2 Integrity constraints

| Control | Detail |
| --- | --- |
| Prescription used by an order cannot be deleted | `DeleteBehavior.Restrict`, pinned by test |
| 38 foreign keys | Verified against the live schema |
| 97 indexes | Includes filtered unique indexes reproducing nullable-unique semantics |
| Money columns | 24 `*Minor` columns, all `int` — the DbContext refuses to build a model otherwise |

### 7.3 Credentials

Connection strings come from configuration; none is committed with a live value.
The account requires `db_owner` on **one** database only — no server-level rights.

---

## 8. Input validation and output encoding

### 8.1 Validation

**Status: Implemented**

| Layer | Mechanism |
| --- | --- |
| Model binding | Data annotations on input models |
| Domain rules | Constants-file allowlists for every constrained string |
| Clinical | 0.25 D step, cylinder-requires-axis — enforced on customer form, staff screen and CSV import alike |
| Money | Converted once at the edge; integer thereafter |
| Uploads | Content-type allowlist, byte-size ceiling, extension forced by processing |

### 8.2 Output encoding

**Status: Implemented**

Razor encodes by default. Two `Html.Raw` usages exist, both reviewed:

| Location | Assessment |
| --- | --- |
| `TryOn/Index.cshtml` — serialised config in `<script type="application/json">` | **Safe, verified live.** `System.Text.Json` escapes `<` and `>` to `<`/`>`, so the block cannot be broken out of. Confirmed by inspecting the rendered page. |
| `Admin/Promotions/Index.cshtml` — promotion code | **Was unsafe. Fixed during this review** — see §9.1 |

### 8.3 File upload

**Status: Implemented**

Content-type allowlist, size ceiling (15 MB, matched by an IIS request limit so
IIS rejects oversized bodies before they reach the application), re-encoding
through SkiaSharp (which discards any non-image payload), server-generated
filenames, and a path-traversal guard resolving every write inside the storage
root.

---

## 9. Findings

### 9.1 Stored XSS via promotion code — **Low — FIXED**

**Location** `dotnet/src/VisionCart.Web/Areas/Admin/Views/Promotions/Index.cshtml`

The promotions list rendered the code with:

```razor
Html.Raw($"<code>{p.Code}</code>")
```

The value is only trimmed and upper-cased on save — there is no character
allowlist — so an administrator could store `<img src=x onerror=…>` as a
promotion code and it would execute in the browser of every staff member who
opened the promotions list.

**Severity: Low.** Requires an authenticated administrator to author it. It
nonetheless crosses a trust boundary (administrator → staff) and was trivially
avoidable.

**Fixed** in this review: the markup now uses Razor's automatic encoding.

**Recommended additionally:** constrain promotion codes to `[A-Z0-9-]` on save,
so the invalid value cannot be stored in the first place.

### 9.2 `style-src 'unsafe-inline'` — Informational

The CSP permits inline styles. Several views use `style="…"` attributes, and the
colour swatches set a custom property inline. Scripts are **not** granted
`unsafe-inline`, so this does not enable script injection; it does slightly
weaken defence against CSS-based exfiltration.

**Recommended:** move remaining inline styles to classes and drop
`'unsafe-inline'` from `style-src`.

### 9.3 Development credentials in the repository — Informational

`appsettings.Development.json` is git-ignored and an
`appsettings.Development.example.json` with blank passwords is committed in its
place. The literal demo passwords appear in exactly two committed files —
`ProductionConfigurationGuard.cs` and its test — where they form the **banned
password list**. They must remain there to do their job.

The production startup guard refuses to boot if any is set as a real seed
password.

### 9.4 Dependency currency — Informational

All packages verified current at the time of writing (EF Core 10.0.11, Identity
10.0.11, Stripe.net 52.3.0, SkiaSharp 4.151.1, xUnit 2.9.3). No automated
vulnerability scanning is configured.

**Recommended:** run `dotnet list package --vulnerable --include-transitive` in
CI.

---

## 10. Production configuration guard

**Status: Implemented** — a control worth calling out

The application **refuses to start** in Production with development
configuration, reporting every problem at once:

| Check | Why |
| --- | --- |
| Connection string empty or LocalDB | LocalDB does not exist on a server |
| Seed password is a published demo password | Would put a known credential on a live site |
| `Email:Driver` is `log` | Every order confirmation silently discarded |
| SMTP configured without a host | Same |
| Stripe listed without a key | Checkout offers a card option that cannot take payment |
| `AllowedHosts` is `*` | Enables cache poisoning and password-reset link forgery |

Each of these fails **silently** otherwise — a site with `Email:Driver=log` looks
entirely healthy. Covered by 10 unit tests.

---

## 11. Control summary

| Control | Status |
| --- | --- |
| Authentication (Identity, PBKDF2) | Implemented |
| Lockout, security stamp, single-use reset | Implemented |
| Role-based authorisation, 3 policies | Implemented |
| Resource ownership scoping | Implemented |
| CSRF, global | Implemented |
| Rate limiting, per client | Implemented |
| HSTS + HTTPS redirect | Implemented |
| Security headers (5) | Implemented |
| CSP with no external origin | Implemented |
| SQL injection protection | Implemented |
| Output encoding | Implemented (one defect fixed, §9.1) |
| File upload validation | Implemented |
| Secrets via environment variables | Implemented |
| Production configuration guard | Implemented |
| Audit trail | Implemented |
| Structured logging with retention | Implemented |
| Health checks | Implemented |
| CORS | Not found — correct for this architecture |
| Automated dependency scanning | **Recommended** |
| Penetration test | **Recommended — not performed** |

---

## 12. Not tested

Stated plainly rather than implied:

- **No penetration test was performed.** This is a code and configuration review.
- **Real proxy behaviour** (`X-Forwarded-For` through IIS/ARR) is configured but
  not exercised against a live proxy.
- **Rate-limit window expiry** is verified to engage and to partition, but not
  across a real five-minute window.
- **TLS configuration** is a hosting concern and was not assessed.

---

*Cross-references: verification evidence in `04_TEST_REPORT.md`; production
configuration in `09_DEPLOYMENT_DOCUMENT.md`.*
