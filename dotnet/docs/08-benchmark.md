# VisionCart — Benchmark Report

Measured 24 August 2026. Both applications built for **production** and run on
the same machine, alternating, with nothing else serving traffic.

Method: `bench/benchmark.py`. 60 samples per endpoint after 8 discarded warm-up
requests; percentiles from the raw sample, never a best-of-N. The first request
of each process is reported separately rather than averaged in, because JIT and
EF model building are a once-per-restart cost, not a per-customer one.

---

## 1. Page latency

Milliseconds, warm. Lower is better.

| Page | Legacy p50 | **.NET p50** | Legacy p95 | **.NET p95** |
| --- | ---: | ---: | ---: | ---: |
| Home | 34.8 | **12.0** | 65.3 | **26.2** |
| Catalogue (`/frames`) | 30.4 | **16.3** | 55.6 | **33.9** |
| Try-on studio | 24.5 | **9.4** | 44.2 | **24.4** |
| Guide page | — | **5.2** | — | **26.0** |
| `/health/ready` | — | **11.5** | — | **32.9** |

Cold first request: legacy 64 ms, .NET 62 ms — effectively identical.

## 2. HTML payload

Bytes over the wire for the same page. This is the part a customer on a phone
pays for.

| Page | Legacy | **.NET** | Change |
| --- | ---: | ---: | ---: |
| Home | 65.0 KB | **13.8 KB** | −79% |
| Catalogue | 58.6 KB | **14.3 KB** | −76% |
| Try-on | 44.6 KB | **19.2 KB** | −57% |

The difference is React hydration payload. Server-rendered Razor ships markup;
the legacy pages ship markup *and* the serialised props needed to rehydrate it
in the browser.

## 3. Sustained load

16 concurrent clients against the home page for 15 seconds.

| | Legacy | **.NET** | Change |
| --- | ---: | ---: | ---: |
| Throughput | 44 req/s | **438 req/s** | **10×** |
| p50 latency | 348.5 ms | **32.6 ms** | −91% |
| p95 latency | 471.9 ms | **66.1 ms** | −86% |
| p99 latency | 621.2 ms | **94.1 ms** | −85% |
| Failures | 0 | 0 | — |

This is the headline result, and it is a difference in kind rather than degree.
Single-request latency differs by about 3×; under concurrency the gap widens to
10× because the two runtimes handle it differently — Node.js serialises work
through one event loop, while ASP.NET Core dispatches across a thread pool.

For a shared host this matters more than the single-request figure. It is the
difference between a shop that degrades under a promotion and one that does not.

## 4. Memory

| | Legacy | **.NET** |
| --- | ---: | ---: |
| Working set | 142.1 MB | 234.8 MB |
| **Private bytes** | 137.7 MB | **122.2 MB** |
| Threads | 30 | 27 |

Reported both ways deliberately, because they disagree. The .NET working set is
larger — it includes shared runtime assemblies mapped into the process — but
**private** memory, which is what a shared host actually charges against a quota,
is lower.

## 5. Deployment footprint

| | Legacy | **.NET** |
| --- | ---: | ---: |
| Artifact | `.next` 89 MB **+ `node_modules` 779 MB** | **77 MB** |
| Files to upload | ~28,000 | **114** |
| Server runtime | A continuously running Node.js process | IIS + .NET Hosting Bundle |
| Build on server | Yes (`npm ci`) | No |

868 MB and 28,000 files versus 77 MB and 114 files. Over FTP to a shared host
that is the difference between a deployment that works and one that times out
halfway.

Of the 77 MB, **57 MB is the MediaPipe WebAssembly runtime and face model** —
served from our own origin rather than a CDN, which is what keeps the customer's
face on their own machine. It carries a one-year cache header, so a returning
visitor pays it once.

## 6. Build and test

| | Time |
| --- | ---: |
| Unit tests (92) | 0.3 s |
| Integration tests (140) | 28 s |
| Full release pipeline (test + publish + strip) | **66 s** |

**232 tests, all passing**, against a database created from nothing during this
session — which also proves the application bootstraps and seeds itself with no
manual step, the property the shared-host deployment depends on.

| Suite | Tests |
| --- | ---: |
| Unit | 92 |
| Integration (service level) | 109 |
| Integration (HTTP level) | 31 |
| **Total** | **232** |

---

## 7. What these numbers do not say

Stated plainly, because a benchmark that hides its confounds is marketing.

**The databases are different.** Legacy runs on SQLite, the port on SQL Server
LocalDB. That is a genuine confound, and it is not neutral: SQLite takes a
single writer lock, so some of the concurrency gap in §3 belongs to the database
rather than the runtime. The comparison is still the honest one to make — it is
the real before and the real after, and SQLite was never going to be the
production database — but the 10× should not be read as "ASP.NET Core is 10×
faster than Node.js".

**The .NET app was measured on Kestrel directly, not through IIS.** In-process
IIS hosting adds a small per-request cost that is not reflected here. It was
chosen precisely because it is the cheapest option (§8 of the deployment guide),
but it is not free.

**Localhost has no network.** Every figure excludes real latency, TLS handshakes
and bandwidth. On a real connection the payload reduction in §2 matters more than
the server-side latency in §1 — 51 KB less HTML is worth more to a customer on
mobile data than 20 ms of server time.

**One machine, one run each.** No statistical treatment beyond percentiles, and
no repetition across days or hardware.

**The try-on studio is not in these numbers.** Its cost is in the browser — the
WebAssembly runtime and the per-frame landmark detection — and none of that is
visible to a server benchmark.

---

## 8. Environment note

Both applications were run on Windows 11 Pro, .NET 10.0.400, Node 20, SQL Server
2019 LocalDB (15.0.4382.1), SQLite via Prisma.

One artefact worth recording, because it cost time and looked exactly like a
product defect: **LocalDB named pipes are scoped to a Windows logon session.**
When the test suite was invoked from a different shell context than the one that
had started the instance, 128 of 140 integration tests failed at once with SQL
connection errors. The suite was never broken — running the instance and the
tests in the same session gives 232/232. If mass integration failures appear
with a *clean* SQL Server error log, check this before suspecting the tests.
