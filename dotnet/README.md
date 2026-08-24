# VisionCart — ASP.NET Core port

The prescription-eyewear shop, rebuilt on ASP.NET Core 10 and SQL Server so it
runs on ordinary Windows/IIS hosting. No Docker, no Linux, no Node.js on the
server, and no long-running process outside the IIS worker.

The original Next.js application is still in the repository root while the
migration settles; both build independently.

---

## Running it locally

**You need:** the .NET 10 SDK and SQL Server LocalDB (installed with Visual
Studio, or from the SQL Server Express installer).

```bash
cd dotnet

# 1. Local configuration. The real file is git-ignored so credentials
#    never reach the remote — fill in any password you like.
cp src/VisionCart.Web/appsettings.Development.example.json \
   src/VisionCart.Web/appsettings.Development.json

# 2. Run. The database is created, migrated and seeded on first start;
#    there is no separate CLI step.
dotnet run --project src/VisionCart.Web
```

The shop is then on <http://localhost:5217>, and the back office at
`/admin` with the account you put in the configuration file.

> **The password you choose matters.** The startup guard refuses to run in
> Production with the published demo passwords, so pick a real one if you ever
> intend to reuse the file.

### Tests

```bash
dotnet test
```

232 tests: 92 unit, 140 integration. The integration tests run against the same
LocalDB instance and create everything they need, so no fixture setup is
required.

> **If most integration tests fail at once** with SQL connection errors and the
> SQL Server log looks clean, the LocalDB instance is almost certainly the
> problem rather than the code — see `docs/08-benchmark.md` §8.

---

## Layout

| Path | What lives there |
| --- | --- |
| `src/VisionCart.Domain` | Entities, constants, the money value object. No dependencies. |
| `src/VisionCart.Application` | Services — pricing, checkout, prescriptions, appointments, privacy. |
| `src/VisionCart.Infrastructure` | EF Core, storage, email, payment and shipping adapters. |
| `src/VisionCart.Web` | Controllers, Razor views, the try-on client. |
| `tests/` | Unit and integration suites. |
| `tools/screenshots/` | Documentation tooling: captures the user-manual figures. |
| `docs/` | Migration reports, deployment guide, benchmark. |

## Documentation

| Document | Covers |
| --- | --- |
| `docs/01-migration-inventory.md` | What was ported, and the decisions taken. |
| `docs/02-vertical-slice.md` | Home → catalogue → cart → checkout → order. |
| `docs/03-tryon-studio.md` | The virtual try-on, and why the face never leaves the browser. |
| `docs/04-back-office.md` | Authentication, email, the staff screens. |
| `docs/05-media-and-data.md` | Media library, import and export. |
| `docs/06-http-harness.md` | The HTTP-level test harness. |
| `docs/07-deployment.md` | **Deploying to IIS / myASP.NET.** |
| `docs/08-benchmark.md` | Measured before and after. |
| `../USER-MANUAL.pdf` | 57-page illustrated manual for customers and staff. |

## Deploying

```powershell
.\publish.ps1
```

Runs the tests, builds the client assets, publishes for `win-x64` and strips
everything that must not ship. Upload the contents of `publish/`. The full
procedure, and every environment variable the host needs, is in
`docs/07-deployment.md`.
