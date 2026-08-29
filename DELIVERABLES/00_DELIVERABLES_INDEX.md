# VisionCart — Deliverables Index

**Project** VisionCart Optical — prescription eyewear shop with virtual try-on
**Prepared** 25 August 2026
**Prepared from** Direct inspection of the project source; every claim verified against code, configuration or a live run

---

## Deliverables

| S.No. | Deliverable | File | Description | Status |
| --- | --- | --- | --- | --- |
| 0 | Deliverables Index | `00_DELIVERABLES_INDEX.md` | This document | Completed |
| 1 | Master Project Prompt | `01_MASTER_PROJECT_PROMPT.md` | All development specifications merged into one structured document | Completed |
| 2 | Features Document | `02_FEATURES_DOCUMENT.md` | Every implemented feature, with flows, rules, tables and roles | Completed |
| 3 | Security Features | `03_SECURITY_FEATURES_DOCUMENT.md` | Verified security review, architecture and findings | Completed |
| 4 | Test Report | `04_TEST_REPORT.md` | 298 tests executed; results, defects, gaps | Completed |
| 5 | Operation Manual | `05_OPERATION_MANUAL.md` | Day-to-day operation for staff, opticians and administrators | Completed |
| 6 | Software Stack | `06_SOFTWARE_STACK.md` | Technologies and verified versions, with architecture flow | Completed |
| 7 | Application Architecture | `07_APPLICATION_ARCHITECTURE.md` | Layers, directory structure, key files | Completed |
| 8 | Database Documentation | `08_DATABASE_DOCUMENTATION.md` | 36 tables, 429 columns, 38 keys, 97 indexes | Completed |
| 9 | Deployment Document | `09_DEPLOYMENT_DOCUMENT.md` | IIS deployment, configuration, rollback | Completed |
| 10 | Customer Delivery Document | `10_CUSTOMER_DELIVERY_DOCUMENT.md` | Customer-facing summary, workflows, screenshots | Completed |
| 11 | Source Code Package | `SOURCE_CODE_PACKAGE.zip` | Complete source, 383 entries, 24.2 MB | Completed |
| 12 | Database Package | `database/` | Schema script plus table, key and index reference | Completed |
| 13 | Screenshots | `screenshots/` | 29 captures of every screen | Completed |
| 14 | User Manual | `USER-MANUAL.pdf` | 57-page illustrated manual, customers and staff | Completed |

---

## Folder structure

```
DELIVERABLES/
├── 00_DELIVERABLES_INDEX.md
├── 01_MASTER_PROJECT_PROMPT.md
├── 02_FEATURES_DOCUMENT.md
├── 03_SECURITY_FEATURES_DOCUMENT.md
├── 04_TEST_REPORT.md
├── 05_OPERATION_MANUAL.md
├── 06_SOFTWARE_STACK.md
├── 07_APPLICATION_ARCHITECTURE.md
├── 08_DATABASE_DOCUMENTATION.md
├── 09_DEPLOYMENT_DOCUMENT.md
├── 10_CUSTOMER_DELIVERY_DOCUMENT.md
├── USER-MANUAL.pdf
├── SOURCE_CODE_PACKAGE.zip
│
├── database/
│   ├── README.md
│   ├── 01_schema.sql              Idempotent, EF-generated, 50 KB
│   ├── 02_table_inventory.md      36 tables · 429 columns
│   ├── 03_relationships.md        38 foreign keys with delete behaviour
│   └── 04_indexes.md              97 indexes, 15 unique, 6 filtered
│
└── screenshots/                   29 PNG captures
```

---

## Which document answers which question

| Question | Document |
| --- | --- |
| What was asked for? | 01 |
| What was built? | 02 |
| Is it secure? | 03 |
| Was it tested, and what failed? | 04 |
| How do staff use it? | 05, `USER-MANUAL.pdf` |
| What is it built on? | 06 |
| How is the code organised? | 07 |
| What is in the database? | 08, `database/` |
| How do I deploy it? | 09 |
| What is being handed over? | 10 |

---

## Project at a glance

| Metric | Value |
| --- | --- |
| Platform | ASP.NET Core 10.0, C#, SQL Server |
| C# source | 84 files · 24,599 lines |
| Razor views | 55 |
| Client TypeScript | 9 modules |
| Application services | 22 |
| Controllers | 8, across 22 route prefixes |
| Database | 36 tables · 429 columns · 38 keys · 97 indexes |
| Automated tests | **298 — all passing** |
| Screens | 29 customer-facing · 19 back office |
| User roles | 4 — Customer, Staff, Optician, Administrator |
| Deployment target | Shared Windows/IIS hosting |
| Paid licences required | **None** |

---

## Quality check

Performed before delivery.

| Check | Result |
| --- | --- |
| All documents present | Pass — 11 documents plus 3 supporting folders |
| No implemented feature undocumented | Pass — feature list derived from source inventory |
| All specification sources merged | Pass — `AGENTS.md`, `docs/01`–`08`, `PROMPT.docx`, in-repository briefs |
| Duplicate specification content merged | Pass — conflicts resolved in favour of implemented code |
| Security claims verified against code | Pass — every control traced to a file; two `Html.Raw` usages individually reviewed |
| Test results not fabricated | Pass — full suite re-run 25 Aug 2026; output quoted verbatim |
| Versions verified | Pass — read from `.csproj`, `package.json`, `dotnet --version` |
| Database documentation matches reality | Pass — generated from the live database and the EF model |
| Source ZIP valid | Pass — 383 entries, integrity verified |
| ZIP free of generated dependencies | Pass — no `node_modules`, `bin`, `obj`, `publish` |
| No secrets in deliverables | Pass — scanned; only documented placeholders present |
| Cross-references correct | Pass |
| Consistent naming and formatting | Pass |

### Verified exclusions from the source package

| Excluded | Confirmed |
| --- | --- |
| `node_modules` | Clean |
| `.git` history | Clean (`.gitignore` retained, correctly) |
| `bin` / `obj` | Clean |
| `publish` output | Clean |
| `.env` files | Clean |
| `appsettings.Development.json` | Clean — the `.example` file is included instead |

---

## Items requiring attention

Recorded rather than glossed over.

| # | Item | Severity | Detail |
| --- | --- | --- | --- |
| 1 | Live try-on tracking has never been measured against a real face | **High** | Geometry is covered by 66 tests, but that is a different claim. Recommend a session with several people and frame shapes before customer release. See `04_TEST_REPORT.md` §8. |
| 2 | No deployment to a real IIS host | **High** | The published artefact was verified running standalone; `web.config` verified by inspection only. See `09_DEPLOYMENT_DOCUMENT.md` §12. |
| 3 | Promotion codes accept any characters | Medium | A stored-XSS route via this was **found and fixed** during this review (`03_SECURITY…` §9.1). Constraining the input to `[A-Z0-9-]` on save would close it at source. |
| 4 | Responsive layout not systematically tested | Medium | Formal QA at 360/390/430 px not completed. |
| 5 | No automated dependency vulnerability scanning | Medium | Recommend `dotnet list package --vulnerable` in CI. |
| 6 | No penetration test | Medium | This was a code and configuration review. |
| 7 | Development database holds accumulated test data | Low | 227 orders, 365 patients from test runs. Reset to clean seed before any demo. |
| 8 | `style-src 'unsafe-inline'` in the CSP | Low | Scripts are not granted it. Removing remaining inline styles would allow it to be dropped. |
| 9 | Try-on calibration has no visual admin screen | Low | Frame anchors are data; adding artwork needs the values set directly. |
| 10 | MediaPipe runtime patch version unverifiable | Informational | The bundle is committed without a version manifest. |

---

## Statement of accuracy

Every factual claim in these deliverables was verified against the project at the
time of writing:

- **Feature claims** come from source inspection, not from documentation.
- **Security claims** each name the file that implements them. Nothing is
  asserted that was not read.
- **Test results** are from a full suite execution on 25 August 2026, after the
  security fix, quoted verbatim.
- **Versions** are read from project files.
- **Database facts** are generated from the live database and the EF model.

Where something could not be verified it is marked **Not Verified** or **Not
Tested — reason: …** rather than assumed. Where a feature is absent it is listed
as **Not implemented** rather than omitted.

---

*Prepared 25 August 2026.*
