# VisionCart — HTTP-Level Test Harness

**Closes the gap recorded in `05-media-and-data.md` §6.**

---

## 1. Why

Every test written before this one called a service directly. That left an
entire layer unexercised — routing, model binding, antiforgery, the
authorisation policies, response headers and static-file content types — and
the gap was not theoretical:

- The media uploader shipped with an **empty antiforgery token**. Every upload
  from a browser was rejected with a 400 while its service-level tests stayed
  green. It was found by hand.
- Before that, the try-on model files 404'd because ASP.NET Core refuses to
  serve extensions it does not recognise. Also found by hand.

Both are HTTP-layer defects, and neither was reachable from a service test.

---

## 2. What it is

`tests/VisionCart.IntegrationTests/Http/VisionCartApp.cs` boots the **real
application** in-process with `WebApplicationFactory<Program>` and drives it
over HTTP. No mocks, no stubbed pipeline: the same middleware, the same
policies, the same Razor views.

Three design decisions are worth recording.

**Sign in once per role, not once per test.** The sign-in endpoint is rate
limited, so a suite that logged in per test would throttle itself and fail for
reasons that have nothing to do with the code. One `HttpClient` per role —
anonymous, customer, staff, admin — is created during fixture initialisation
and reused.

**Redirects are not followed.** `AllowAutoRedirect = false`, because in most
authorisation tests the redirect *is* the assertion: an anonymous request to
the back office must **become** a trip to `/login`, and that is invisible once
the handler has already followed it.

**The antiforgery token is scraped from the rendered HTML.** Reading it from
the framework instead would have passed happily while the view emitted a broken
one — which is exactly the bug that prompted this work.

The staff passwords are read from the application's own configuration rather
than hardcoded, because the seeder never rewrites an existing account's
password and a literal here would silently drift out of date.

### Test-only plumbing

`ClientIpOverrideStartupFilter` lets a test present itself as coming from a
chosen IP. TestServer reports no remote address, so without it every in-process
request lands in the same rate-limiting partition and a per-client limit is
indistinguishable from a global one. It is registered through
`ConfigureTestServices`; the application pipeline is untouched.

Assembly-level parallelisation is disabled. The HTTP harness and the
service-level tests share one database and one pool of frame stock; run
concurrently, a stock top-up in one is an unexplained failure in the other.

---

## 3. What it covers — 31 tests

| Area | Covered |
| --- | --- |
| **Authorisation** | Anonymous is redirected to `/login` from six back-office routes; a signed-in customer is refused with `/error/403`; staff are admitted; **staff are refused the audit trail while an administrator is admitted** (§9) |
| **Clinical data** | `patients` and `prescriptions` exports are unreachable without signing in, and do not return `text/csv` |
| **Antiforgery** | A POST without a token is rejected with 400; every page that posts renders a real token; an upload *with* a token reaches the image pipeline and lands in the library |
| **Static assets** | `.task` and `.wasm` are served with the right content type — the exact failure that silently broke the try-on studio |
| **Security headers** | `nosniff`, `X-Frame-Options: DENY`, and a CSP present on every response |
| **Privacy (§7)** | The CSP names **no external origin**, so a future edit cannot quietly reintroduce a CDN into the face-processing page |
| **Privacy (§10)** | No patient link carries an email address, date of birth or any diopter field in its URL; a missing page leaks no stack trace |
| **Rate limiting** | The limiter engages; **one visitor cannot throttle everybody else**; a failed sign-in reveals nothing about whether the account exists |

The upload test cleans up after itself — it deletes the image it uploaded — and
the fixture deletes its test customer on disposal. This suite runs against a
database a developer also browses.

---

## 4. The defect it found on its first run

### Every rate limiter had a single global bucket

```csharp
options.AddFixedWindowLimiter("auth", limiter => { limiter.PermitLimit = 8; … });
```

`AddFixedWindowLimiter` creates **one** limiter shared by every visitor, not one
per client. The sign-in policy allowed 8 attempts per 5 minutes *in total,
across the entire site*.

That inverts the control's purpose. A brute-force defence became a
denial-of-service vector: eight bad passwords from any one person — or one
script — would lock **every other customer** out of signing in for five
minutes. The same flaw applied to `checkout` (20) and `upload` (30), so one
customer checking out could block the shop's tills.

The fix partitions each policy per client:

```csharp
PerClient("auth",     permitLimit: 8);
PerClient("checkout", permitLimit: 20);
PerClient("upload",   permitLimit: 30);
```

A signed-in visitor is keyed by account, so someone behind a shared office NAT
is not throttled by a colleague. Anonymous requests fall back to the connecting
address. A request with neither shares one last bucket — the safe default, since
it can only ever be more restrictive.

The regression test was verified the same way as the others: the global buckets
were reinstated, `One_visitor_cannot_throttle_everybody_else` was confirmed to
fail, and the fix was restored.

---

## 5. Two failures that were the tests' fault, not the code's

Recorded because both looked like security findings at first glance and neither
was.

**`/login` returned 302 for a signed-in admin.** The token test used the admin
client for every path; an already-authenticated visitor is redirected away from
the sign-in form. Split into its own case using the anonymous client.

**The two sign-in refusals rendered different HTML.** This looked like a user
enumeration leak. It was not: the form legitimately echoes back the address
that was typed, and the antiforgery token is regenerated per request. Neither
tells an attacker anything. The test now compares the **validation message
shown to the visitor**, which is the thing that would actually leak — and it is
identical for an unknown account and a wrong password.

---

## 6. Verification

After the rate-limiter change, sign-in was re-checked against the running
application, not just the harness:

| Check | Result |
| --- | --- |
| `GET /login` renders a token | 155 characters |
| `POST /login` with seeded admin credentials | `302` |
| `GET /admin` with the session cookie | `200` |
| `GET /admin/media` | `200` |

Database checked afterwards for residue: no leftover test accounts, no live
media rows, no orphaned files.

---

## 7. Test totals

| Project | Before | Now |
| --- | --- | --- |
| `VisionCart.UnitTests` | 73 | 73 |
| `VisionCart.IntegrationTests` | 80 | **111** |
| **Total** | 153 | **184**, all passing |

---

## 8. What this harness still does not cover

Stated plainly, per the brief's §30:

- **Per-IP isolation is proven only through the test-only IP override.** Real
  proxy and load-balancer behaviour (`X-Forwarded-For`, `UseForwardedHeaders`)
  is not exercised, and matters on IIS behind a reverse proxy.
- **No JavaScript runs.** The harness posts what the browser *would* post; it
  does not execute `media-uploader.js`. The uploader's own logic — sequential
  queueing, per-file error reporting — is still only verified by driving a real
  browser.
- **The try-on studio is not exercised end to end.** The harness proves its
  assets are served with usable content types; it cannot prove the camera
  pipeline works. That needs a real browser with a real camera.
- **Rate-limit windows are not tested across time.** The tests prove the limiter
  engages and is partitioned, not that it resets after five minutes.

---

## 9. Remaining work

- Customer address book
- Appointments module
- Data-subject correction and erasure flow
- IIS deployment validation and the deployment manual
- Final audit sweep
