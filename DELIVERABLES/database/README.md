# Database Package

Everything needed to create the VisionCart database.

Generated **25 August 2026** from the live development database and the EF Core
model.

| File | Contents |
| --- | --- |
| `01_schema.sql` | Complete **idempotent** schema script — safe to run against an empty or partly-migrated database |
| `02_table_inventory.md` | All 36 tables, 429 columns, with types and nullability |
| `03_relationships.md` | All 38 foreign keys with delete behaviour |
| `04_indexes.md` | All 97 indexes, including the 6 filtered uniques |

## Creating the database

**Preferred — let the application do it.** Point the connection string at an
empty database and start the app. Tables are created, migrated and seeded on
first run. Shared hosting offers no command line, so this path is the one the
application is designed around.

**Alternative — run the script:**

```bash
sqlcmd -S <server> -d <database> -i 01_schema.sql
```

The script is idempotent: every object is guarded, so re-running it is safe.

**Developer machines — EF tooling:**

```bash
cd dotnet
dotnet ef database update --project src/VisionCart.Infrastructure \
                          --startup-project src/VisionCart.Infrastructure
```

## What is not here

- **No data.** No seed rows, no customer data, no credentials. The application
  seeds reference data itself on first start.
- **No connection string.** Supply it through
  `ConnectionStrings__DefaultConnection`.

## Permissions

The application account needs `db_owner` on **this one database** — it creates
tables on first run. It needs no server-level permission.

## Backup

Back up the database **and** `wwwroot/uploads` together. Product photographs live
in the folder and are referenced by `MediaAsset` rows; restoring one without the
other gives a catalogue of broken images.
