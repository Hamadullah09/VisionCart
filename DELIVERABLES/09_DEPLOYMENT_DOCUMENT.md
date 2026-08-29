# VisionCart — Deployment Document

**Prepared** 25 August 2026
**Target** Shared Windows hosting with IIS (myASP.NET and equivalents)
**Source** Verified against `publish.ps1`, `web.config`, `ProductionConfigurationGuard.cs` and `appsettings.Production.json`

> A fuller narrative version, including the reasoning behind each choice, is in
> `dotnet/docs/07-deployment.md`. This document is the procedure.

---

## 1. Prerequisites

### On the host

| Requirement | Detail |
| --- | --- |
| IIS | With the **.NET 10 Hosting Bundle** installed |
| App pool | .NET CLR version **"No Managed Code"** |
| Platform | Windows x64 (x86 if the pool is 32-bit) |
| SQL Server | 2019 or later, one empty database |
| Database account | `db_owner` on that one database; no server-level rights |
| SMTP | Any host, for order confirmations |

**Not required:** Docker · Kubernetes · Linux · Node.js on the server ·
PostgreSQL · SQLite · any external worker process.

### On the build machine

| Requirement | Detail |
| --- | --- |
| .NET SDK | 10.0.400 or later |
| PowerShell | 5.1 or later |
| Node.js | Only if client assets need rebuilding |

---

## 2. Building the package

```powershell
cd dotnet
.\publish.ps1
```

This runs the tests, builds the client assets, publishes for `win-x64`, and
strips everything that must not ship. Output is `dotnet\publish` — upload its
**contents** to the site root.

| Switch | Use |
| --- | --- |
| `-SkipTests` | Skip the test run. Only when you have just run them. |
| `-Runtime win-x86` | The app pool has *Enable 32-Bit Applications* set to True |
| `-Output <path>` | Write the package elsewhere |

### 2.1 What the script removes

| Removed | Why |
| --- | --- |
| `appsettings.Development.json` | Carries a LocalDB connection string and demo passwords |
| `*.pdb` | Debug symbols |
| `*.gz`, `*.br` | Precompressed variants only `MapStaticAssets` serves; this app uses `UseStaticFiles` so nothing reads them |

### 2.2 Package size

| Component | Size |
| --- | --- |
| Application and .NET dependencies | ~8 MB |
| SkiaSharp native library | ~12 MB |
| MediaPipe WebAssembly + face model | ~57 MB |
| **Total** | **~77 MB** |

The WebAssembly runtime and face model are the bulk, and they are served from
your own origin **on purpose** — that is what keeps the customer's face on their
own machine. One-off download per visitor; the assets carry a one-year cache
header and IIS compresses them.

> An untargeted `dotnet publish` produces ~205 MB, because SkiaSharp ships native
> binaries for Linux, macOS and ARM that IIS will never load. `publish.ps1`
> always passes a runtime identifier for this reason.

---

## 3. Database setup

Create an **empty** database in the host's control panel and note the connection
string. Nothing else is needed: **the application migrates and seeds itself on
first start**, because shared hosting gives you no command line to run
`dotnet ef database update` from.

If you prefer to create the schema first, `DELIVERABLES/database/01_schema.sql`
is idempotent.

---

## 4. Configuration — **requires manual setup**

`appsettings.Production.json` is committed and contains **no secrets**. Every
sensitive value comes from an environment variable set in the host's control
panel.

ASP.NET Core maps a double underscore to a configuration colon, so
`ConnectionStrings__DefaultConnection` sets `ConnectionStrings:DefaultConnection`.

### 4.1 Required — the application refuses to start without these

| Variable | Example |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | `Server=sql.example.net;Database=visioncart;User Id=…;Password=…;TrustServerCertificate=True` |
| `AllowedHosts` | `shop.example.com;www.shop.example.com` |
| `Email__Host` | `smtp.yourprovider.com` |
| `Store__AppUrl` | `https://shop.example.com` |

### 4.2 Also set

| Variable | Notes |
| --- | --- |
| `Email__Username`, `Email__Password` | SMTP credentials |
| `Email__FromAddress`, `Email__FromName` | Appears on every customer email |
| `Store__Name`, `Store__Currency`, `Store__CurrencySymbol` | |
| `Tax__RateBps` | Basis points — 1700 is 17% |
| `Payments__Providers` | `cod,bank_transfer` or add `stripe` |
| `Payments__StripeSecretKey` | **Only if** Stripe is listed above |

> **Never commit a production secret.** Everything above belongs in the host's
> environment panel, not in a file.

---

## 5. Creating the first administrator — **requires manual setup**

There are **no default credentials**. The seeded demo passwords are rejected by
the startup guard, so they cannot reach a live site even by accident.

1. Set `Seed__Enabled=true`, `Seed__AdminEmail`, `Seed__AdminPassword` (a real
   password)
2. Restart the site and sign in at `/login`
3. **Set `Seed__Enabled=false` and delete the two seed variables.** Restart.

Step 3 matters: leaving the password in the host's environment panel means anyone
with control-panel access has the administrator password in plain text.

---

## 6. Uploading

Copy the **contents** of `dotnet\publish` into the site root — usually `wwwroot`
or `httpdocs` in the host's file manager, not a subfolder.

Two folders must exist and be **writable by the application pool identity**:

| Folder | Holds |
| --- | --- |
| `wwwroot/uploads` | Product photography and try-on overlays |
| `logs` | The rolling application log |

`publish.ps1` creates both empty so they survive the upload.

### 6.1 Updating an existing site

Drop a file named `app_offline.htm` in the site root. IIS shuts the application
down gracefully; replace the files; delete it. Without this the DLLs are locked
and the upload half-fails.

---

## 7. Verifying the deployment

Do not stop at "the home page loads."

| Check | Expect |
| --- | --- |
| `GET /health/live` | `200 Healthy` |
| `GET /health/ready` | `{"status":"Healthy","checks":{"database":"Healthy","email-outbox":"Healthy"}}` |
| `GET /models/face_landmarker.task` | `200`, `application/octet-stream`, ~3.7 MB |
| `GET /wasm/vision_wasm_internal.wasm` | `200`, `application/wasm`, ~11 MB |
| `GET /` headers | CSP present; no `X-Powered-By`; no `Server` |
| Sign in at `/login` | Reaches `/admin` |
| `/try-on` in a real browser | Camera prompt appears and the mirror starts |
| Place a test order | Confirmation email arrives |

**`/health/ready` is the one that matters.** "The site returns 200" and "the site
can take an order" are different questions, and the commonest shared-hosting
failure — an application that starts perfectly and cannot reach SQL Server —
answers the first one correctly.

### 7.1 The startup guard

Starting in Production with development configuration is **refused**, and every
problem is reported at once:

```
VisionCart refused to start because its production configuration is incomplete:
  - ConnectionStrings:DefaultConnection still points at LocalDB, which does not exist on a server.
  - Email:Driver is 'log', so no customer would receive an order confirmation.
  - AllowedHosts is '*'. Set it to the site's own domain(s).
See docs/07-deployment.md. Nothing has been started and no data has been touched.
```

Each of those failures is otherwise **silent**. A site with `Email:Driver=log`
looks entirely healthy and quietly discards every order confirmation.

---

## 8. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| **500.19** | App pool set to a CLR version. It must be **"No Managed Code"** — ASP.NET Core runs through the ASP.NET Core Module, not the CLR. |
| **500.30 on start** | Read the guard message; it names every missing setting. If nothing is logged, see §8.1. |
| **`/wasm/…` returns 404** | IIS is serving the file directly and does not know the type. The `staticContent` block in `web.config` fixes it — confirm the file uploaded and the host did not strip it. |
| **Upload fails with a permissions error** | `wwwroot/uploads` is not writable by the pool identity. |
| **Nothing in `logs/`** | Same cause, for the `logs` folder. |
| **First request each morning is slow** | Shared hosts stop an idle app pool after ~20 minutes. Set idle timeout to 0 if the host allows it. |
| **Mail not arriving** | Check `/health/ready`. `Degraded` on `email-outbox` means sends are failing. |
| **One visitor's failed logins throttle everyone** | Would indicate the rate limiter is not partitioning — verify `UseForwardedHeaders` is reached before `UseRateLimiter`. |

### 8.1 When the site will not start at all

The application never gets far enough to write its own log. Set
`stdoutLogEnabled="true"` in `web.config`, restart, read `logs/stdout_*.log`,
then **set it back to `false`** — that channel is unbuffered and grows without
limit, which on a quota-limited plan eventually takes the site down.

---

## 9. Backups

The host's database backup covers the tables. It does **not** cover
`wwwroot/uploads`, which holds every product photograph and try-on overlay.
Those files are referenced by `MediaAsset` rows, so a restored database with a
missing uploads folder gives you a catalogue of broken images.

**Back up both together, or neither is a backup.** Confirm periodically that you
can actually restore them.

---

## 10. Rollback

Keep the previous `publish` folder. To roll back: drop `app_offline.htm`, replace
the files, remove it.

The database is the part that does not roll back. **Migrations in this
application are additive** — no migration drops a column — so an older build runs
against a newer schema. That property is what makes the above safe; preserve it
when adding migrations.

---

## 11. Items requiring manual configuration

Collected for the deployment engineer:

| # | Item | Section |
| --- | --- | --- |
| 1 | Create an empty database and obtain the connection string | §3 |
| 2 | Set all required environment variables | §4.1 |
| 3 | Set optional variables for email, tax, payments | §4.2 |
| 4 | Seed the first administrator, then **remove the seed variables** | §5 |
| 5 | Ensure `wwwroot/uploads` and `logs` are writable | §6 |
| 6 | Set the app pool to "No Managed Code" | §8 |
| 7 | Configure TLS at the host (do not add HSTS there — the app already sends it) | §1 |
| 8 | Set up backups covering database **and** uploads | §9 |

---

## 12. Not verified

- **No deployment to a real IIS host has been performed.** The published artefact
  was verified running standalone; `web.config` is verified correct by inspection
  only.
- **Real proxy behaviour** (`X-Forwarded-For` through IIS/ARR) is configured but
  not exercised against a live proxy. The per-client rate limiter depends on it.
- **TLS configuration** is a hosting concern and was not assessed.

---

*Cross-references: narrative version in `dotnet/docs/07-deployment.md`; schema in
`DELIVERABLES/database/`; verification checklist basis in `04_TEST_REPORT.md`.*
