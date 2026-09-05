# VisionCart — Deployment to myASP.NET (IIS)

Everything needed to put this application on a shared Windows host, and the
reasoning behind the choices, so the next person can vary them safely.

**What the host must provide:** IIS, the .NET 10 Hosting Bundle, and a SQL
Server database. Nothing else. There is no Docker, no Kubernetes, no Linux, no
Node.js on the server, no PostgreSQL, no SQLite, and no long-running process
outside the IIS worker.

---

## 1. Build the package

```powershell
.\publish.ps1
```

That runs the tests, builds the client assets, publishes for `win-x64`, and
strips anything that must not ship. The result is `.\publish` — upload its
**contents** to the site root.

| Switch | Use |
| --- | --- |
| `-SkipTests` | Skip the test run. Only when you have just run them. |
| `-Runtime win-x86` | The host's app pool has *Enable 32-Bit Applications* set to True. |
| `-Output <path>` | Write the package somewhere else. |

### Why the package is the size it is

| | Size |
| --- | --- |
| Application and .NET dependencies | ~8 MB |
| SkiaSharp native library (image processing) | ~12 MB |
| MediaPipe WebAssembly runtimes + face model | ~57 MB |
| **Total** | **~77 MB** |

The WebAssembly runtime and the face model are the bulk of it, and they are
served from your own origin rather than a CDN **on purpose**: that is what keeps
the customer's face on their own machine. It is a one-off download per visitor —
the assets carry a one-year cache header and IIS compresses them on the way out.

An untargeted `dotnet publish` produces ~205 MB, because SkiaSharp ships native
binaries for Linux, macOS and ARM that IIS will never load. `publish.ps1` always
passes a runtime identifier for this reason.

---

## 2. Create the database

Create an empty SQL Server database in the host's control panel and note the
connection string. Nothing else is needed: **the application migrates itself on
first start**, which is deliberate — a shared host gives you no command line to
run `dotnet ef database update` from.

The account needs `db_owner` on that one database (it creates tables on first
run). It needs no server-level permission.

---

## 3. Configuration

`appsettings.Production.json` is committed and contains **no secrets**. Every
sensitive value comes from an environment variable set in the host's control
panel.

ASP.NET Core maps a double underscore to a configuration colon, so
`ConnectionStrings__DefaultConnection` sets `ConnectionStrings:DefaultConnection`.

### Required — the application refuses to start without these

| Variable | Example |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | `Server=sql.myasp.net;Database=visioncart;User Id=…;Password=…;TrustServerCertificate=True` |
| `AllowedHosts` | `shop.example.com;www.shop.example.com` |
| `Email__Host` | `smtp.yourprovider.com` |
| `Store__AppUrl` | `https://shop.example.com` |

### Also set

| Variable | Notes |
| --- | --- |
| `Email__Username`, `Email__Password` | SMTP credentials |
| `Email__FromAddress`, `Email__FromName` | Appears on every customer email |
| `Store__Name`, `Store__Currency`, `Store__CurrencySymbol` | |
| `Tax__RateBps` | Basis points — 1700 is 17% |
| `Payments__Providers` | `cod,bank_transfer` or add `stripe` |
| `Payments__StripeSecretKey` | Only if Stripe is listed above |

### Bringing the site up before the mailbox exists

`Email__Driver=log` is refused in Production, because a site that writes every
order confirmation to a log file looks perfectly healthy from the outside. If
you genuinely need the site up before SMTP is arranged, say so by name:

| Variable | Value |
| --- | --- |
| `Email__Driver` | `log` |
| `Email__AllowLogDriverInProduction` | `true` |

The application then starts and logs a warning on **every** start, so the
compromise cannot be forgotten. Understand what it costs first: the log sender
marks each message **sent**, so mail written during this window is never
delivered — configuring SMTP afterwards does not go back for it. Clear both
variables once real mail works.

### The startup guard

Starting in Production with development configuration is refused, and the
process reports every problem at once:

```
VisionCart refused to start because its production configuration is incomplete:
  - ConnectionStrings:DefaultConnection still points at LocalDB, which does not exist on a server.
  - Email:Driver is 'log', so no customer would receive an order confirmation.
  - AllowedHosts is '*'. Set it to the site's own domain(s).
```

This exists because each of those failures is silent. A site with
`Email:Driver=log` looks completely healthy and quietly discards every order
confirmation; `AllowedHosts=*` makes password-reset link forgery possible. The
guard reports all problems together rather than one per deployment, because each
redeploy on a shared host costs minutes.

---

## 4. Creating the first administrator

There are no default credentials. The seeded demo passwords are **rejected** by
the startup guard, so they cannot reach a live site even by accident.

Create the first account by seeding once, deliberately:

1. Set `Seed__Enabled=true`, `Seed__AdminEmail`, `Seed__AdminPassword`
   (a real password — see the banned list in `ProductionConfigurationGuard`).
2. Restart the site and sign in at `/login`.
3. **Set `Seed__Enabled=false` and delete the two seed variables.** Restart.

Step 3 matters: leaving the password in the host's environment panel means
anyone with control-panel access has the administrator password in plain text.

---

## 5. Upload

Copy the **contents** of `.\publish` into the site root (usually `wwwroot` or
`httpdocs` in the host's file manager — not a subfolder).

Two folders must exist and be writable by the application pool identity:

| Folder | Holds |
| --- | --- |
| `wwwroot/uploads` | Product photography and try-on overlays |
| `logs` | The rolling application log |

`publish.ps1` creates both empty so they survive the upload. If the host resets
permissions, an upload fails with an access error and the log stops being
written — both are visible in the control panel's file manager.

### Updating an existing site

`app_offline.htm` is the standard IIS mechanism: drop a file with that name in
the site root, IIS shuts the application down gracefully, you replace the files,
then delete it. Without it, the DLLs are locked and the upload half-fails.

---

## 6. Verify the deployment

Do not stop at "the home page loads".

| Check | Expect |
| --- | --- |
| `GET /health/live` | `200 Healthy` — the process is up |
| `GET /health/ready` | `{"status":"Healthy","checks":{"database":"Healthy","email-outbox":"Healthy"}}` |
| `GET /models/face_landmarker.task` | `200`, `application/octet-stream`, ~3.7 MB |
| `GET /wasm/vision_wasm_internal.wasm` | `200`, `application/wasm`, ~11 MB |
| `GET /` response headers | `Content-Security-Policy` present, no `X-Powered-By`, no `Server` |
| Sign in at `/login` | Reaches `/admin` |
| `/try-on` in a real browser | Camera prompt appears and the mirror starts |

`/health/ready` is the one that matters. "The site returns 200" and "the site can
take an order" are different questions, and the most common shared-hosting
failure — an application that starts perfectly and cannot reach SQL Server —
answers the first one correctly.

If `/wasm/...` returns 404, IIS is serving those files directly and does not know
the type. The `staticContent` block in `web.config` fixes it; confirm the file
uploaded and that the host has not stripped it.

---

## 7. Reading the logs

Shared IIS hosting has no console, so the application writes its own rolling log
to `logs/visioncart-YYYY-MM-DD.log`. Files roll daily and at 20 MB, and anything
older than 14 days is deleted — a shared plan has a disk quota, and exceeding it
takes the site down.

Writing never blocks a request: entries go onto a bounded queue drained by one
background thread, and if the disk stalls, log lines are dropped and counted
rather than slowing checkout.

`logs` is in `web.config`'s `hiddenSegments`, so it is not reachable over HTTP.

**If the site will not start at all**, the application never gets far enough to
log. Set `stdoutLogEnabled="true"` in `web.config` temporarily, restart, read
`logs/stdout_*.log`, then set it back to `false` — that channel is unbuffered and
grows without limit.

---

## 8. Things that are easy to get wrong

**The app pool must not be set to "No Managed Code" — it must be.** ASP.NET Core
runs through the ASP.NET Core Module, not the CLR, so the .NET CLR version should
be *No Managed Code*. Setting it to v4.0 breaks the site with a 500.19.

**Idle timeout.** Shared hosts shut an app pool down after ~20 minutes idle. The
first request afterwards pays the startup cost — a few seconds, including
migration checks. If the host allows it, set the idle timeout to 0 and the
recycle to a fixed quiet hour. The email outbox drains inside the worker process,
so while the pool is stopped nothing is sent; queued mail goes out on the next
start, which is why mail is queued rather than sent inline.

**In-process hosting is not available to you if the host shares one application
pool across your sites.** IIS permits exactly one in-process ASP.NET Core
application per pool, and shared hosts routinely put every site you own in the
same one. Setting `hostingModel="inprocess"` there does not degrade — the module
refuses to start the application at all, and it surfaces as **HTTP 500.30 with
an empty stdout log**, which is close to undiagnosable from outside. myASP.NET
exposes this as *Manage Website → .NET Core Mode*; if it reads `InProcess` and
you host more than one Core app, that is your answer. `web.config` ships
`outofprocess` for this reason. Switch back the moment the application has a
pool to itself: in-process is roughly twice as fast, because requests skip a
loopback hop.

**The control panel may own `web.config`.** myASP.NET's file-manager editor
sanitises the file on save to a permitted subset: `hostingModel`,
`environmentVariables` and `stdoutLogEnabled` survive, while whole sections —
`httpProtocol`, `staticContent`, `security` — and attributes like
`startupTimeLimit` are silently dropped. Its *Error Logs* toggle rewrites the
file too, re-asserting `stdoutLogEnabled="true"` and reverting anything else you
changed. Turn that off before editing, verify every change by reopening the
file, and expect the hardening in this repository's `web.config` not to survive.
Losing it costs less than it looks: the module handles every path, static files
are served only from `wwwroot`, and `appsettings*.json`, `web.config` and the
DLLs are all unreachable over HTTP regardless. What you lose is the
`X-Powered-By` header removal.

**Uploading over a running application fails.** IIS holds the DLLs open, `STOR`
returns FTP 550, and the transfer stops part-way leaving a half-updated site.
`deploy-ftp.ps1` handles this with `app_offline.htm`. If you upload by hand, put
that file in the site root first and delete it afterwards.

**A site-scoped FTP account may not be able to overwrite.** An account created
in the panel can create files but is not necessarily permitted to replace ones
the panel's own process wrote. The symptom is FTP 550 on the first existing
file while a brand-new file uploads fine. Clear the directory first, or grant
the account overwrite rights.

**Rate limiting behind a proxy.** The limiters partition per client, keyed on the
authenticated user or the connecting IP. Behind a reverse proxy every request
appears to come from the proxy unless `X-Forwarded-For` is honoured —
`UseForwardedHeaders` is configured for this. In-process IIS hosting sets the
client address directly, so this is already correct; if you move to a different
proxy, verify it, or one visitor's failed logins will throttle everybody.

**TLS.** The application sets HSTS and redirects HTTP to HTTPS in Production. Do
not enable HSTS at the host level as well — a duplicated header is a
specification violation and some browsers ignore both.

---

## 9. Backups

The host's database backup covers the tables. It does **not** cover
`wwwroot/uploads`, which holds every product photograph and try-on overlay. Those
files are referenced by `MediaAsset` rows, so a restored database with a missing
uploads folder gives you a catalogue of broken images.

Back up both together, or neither is a backup.

---

## 10. Rollback

Keep the previous `publish` folder. To roll back: drop `app_offline.htm`, replace
the files, remove it.

The database is the part that does not roll back. Migrations are additive in this
application — no migration drops a column — so an older build runs against a
newer schema. That is deliberate, and it is what makes the rollback above safe;
preserve the property when adding migrations.
