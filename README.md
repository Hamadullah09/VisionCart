# VisionCart Optical

A prescription eyewear shop: online catalogue, virtual try-on, lens builder,
prescription handling, orders, an appointment diary and a full back office.

Built on **ASP.NET Core 10** and **SQL Server**, and deployable to ordinary
Windows/IIS shared hosting — no Docker, no Linux, no Node.js on the server, and
no long-running process outside the IIS worker.

## Running it

```bash
cd dotnet
cp src/VisionCart.Web/appsettings.Development.example.json \
   src/VisionCart.Web/appsettings.Development.json
dotnet run --project src/VisionCart.Web
```

The database is created, migrated and seeded on first start. Full instructions,
including the back-office sign-in, are in [`dotnet/README.md`](dotnet/README.md).

## Documentation

| Document | Covers |
| --- | --- |
| [`dotnet/README.md`](dotnet/README.md) | Running it, layout, tests |
| [`dotnet/docs/07-deployment.md`](dotnet/docs/07-deployment.md) | Deploying to IIS / myASP.NET |
| [`dotnet/docs/`](dotnet/docs/) | Migration reports, try-on architecture, benchmark |
| `USER-MANUAL.pdf` | 57-page illustrated manual for customers and staff |
| `MANUAL.pdf` | Architecture and completion report |

## History

This began as a Next.js application and was ported to ASP.NET Core so it could
run on the target hosting. The original has been removed now the port is
complete; it remains in git history should you need to consult it.
