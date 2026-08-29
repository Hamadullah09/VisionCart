# VisionCart — Test Report

| | |
| --- | --- |
| **Project** | VisionCart Optical |
| **Test date** | 25 August 2026 |
| **Prepared by** | QA review, full suite execution |
| **Build** | `net10.0`, SDK 10.0.400, Debug |
| **Result** | **298 / 298 passed, 0 failed, 0 skipped** |

---

## 1. Testing environment

| Component | Value |
| --- | --- |
| OS | Windows 11 Pro 10.0.26200 |
| .NET SDK | 10.0.400 |
| Runtime | .NET 10.0 |
| Database | SQL Server LocalDB, instance `VisionCartDev` |
| Test framework | xUnit 2.9.3 · `Microsoft.NET.Test.Sdk` 17.14.1 |
| HTTP harness | `Microsoft.AspNetCore.Mvc.Testing` 10.0.11 |
| Client tests | `node --test`, Node 24.15.0 |
| Browser (manual) | Chrome, driven via `playwright-core` |

---

## 2. Test strategy

Four layers, each answering a question the layer below cannot:

| Layer | Count | Answers |
| --- | --- | --- |
| **Unit** | 92 | Is the logic correct in isolation? |
| **Integration** | 109 | Do the services behave correctly against a real database? |
| **HTTP** | 31 | Does it work over the wire — routing, binding, antiforgery, authorisation? |
| **Client** | 66 | Is the try-on geometry correct, without a camera? |

The HTTP layer exists because two real defects survived a green service-level
suite: the media uploader's antiforgery token and the try-on asset content
types. Service tests never cross the HTTP boundary and so could not see either.

---

## 3. Execution summary

```
VisionCart.UnitTests          92 passed,   0 failed,  0 skipped   (0.3 s)
VisionCart.IntegrationTests  140 passed,   0 failed,  0 skipped  (28.0 s)
geometry.test.ts              42 passed,   0 failed
pose.test.ts                  24 passed,   0 failed
────────────────────────────────────────────────────────────────
TOTAL                        298 passed,   0 failed,  0 skipped
```

Run after the security fix in §9.1 of the security document, so these results
reflect the delivered code.

---

## 4. Test suites

| Suite | Tests | Module |
| --- | --- | --- |
| `MoneyTests` | 26 | Money value object |
| `CuidTests` | — | Identifier generation |
| `ProductionGuardTests` | 10 | Production configuration guard |
| `ViewConventionTests` | ~47 | Razor lint (one case per view) |
| `SchemaMigrationTests` | 9 | Schema fidelity |
| `CheckoutSideEffectTests` | 15 | Checkout side effects |
| `PromotionRuleTests` | 13 | Promotion rules |
| `BackOfficeTests` | 16 | Order and prescription workflow |
| `CsvParserTests` | 11 | CSV parsing |
| `DataTransferTests` | — | Import, export, media |
| `AddressBookTests` | 6 | Address book |
| `AppointmentTests` | 8 | Clinic diary |
| `PrivacyTests` | 12 | Data-subject rights |
| `HttpAuthorizationTests` | 12 | Route authorisation |
| `HttpAntiforgeryTests` | 5 | CSRF |
| `HttpAssetTests` | 6 | Static assets, headers, CSP |
| `HttpPrivacyTests` | 2 | URL privacy, error leakage |
| `HttpRateLimitTests` | 3 | Throttling, enumeration |
| `geometry.test.ts` | 42 | Frame placement, auto-fit |
| `pose.test.ts` | 24 | Head pose, smoothing |

---

## 5. Test cases — representative

### 5.1 Money and pricing

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| M-01 | Money | 24 `*Minor` columns are all `int` | Model build succeeds; count = 24 | 24 int columns | **Pass** | — |
| M-02 | Money | `ToMinor`/`FromMinor` round-trip | No drift | No drift | **Pass** | — |
| M-03 | Money | Formatting is culture-independent | Same output under any culture | Same | **Pass** | — |
| P-01 | Pricing | Cart re-priced from database on read | Tampered payload ignored | Ignored | **Pass** | — |

### 5.2 Checkout

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| C-01 | Checkout | Order placement decrements stock exactly once | before − 1 | before − 1 | **Pass** | — |
| C-02 | Checkout | Stock never goes negative | ≥ 0 | ≥ 0 | **Pass** | — |
| C-03 | Checkout | Order lines carry a frozen snapshot | Snapshot present | Present | **Pass** | — |
| C-04 | Checkout | Prescription becomes a versioned record awaiting an optician | Pending verification | Pending | **Pass** | — |
| C-05 | Checkout | A returning guest keeps the same patient file | Same file no. | Same | **Pass** | — |
| C-06 | Checkout | Off-step diopter refused (−2.13 D) | Rejected | Rejected | **Pass** | — |
| C-07 | Checkout | Unavailable payment method refused | Rejected | Rejected | **Pass** | — |
| C-08 | Checkout | One offline payment row is left | Exactly one | One | **Pass** | — |

### 5.3 Back office

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| B-01 | Orders | Cancelling returns frames to stock | Stock restored | Restored | **Pass** | — |
| B-02 | Orders | Dispatch records courier and emails customer | Both | Both | **Pass** | — |
| B-03 | Rx | Lenses cannot be ready while Rx unverified | Refused | Refused | **Pass** | — |
| B-04 | Rx | Rejection records reason and emails customer | Both | Both | **Pass** | — |
| B-05 | Rx | Unfillable Rx refused at the optician screen too | Refused | Refused | **Pass** | — |
| B-06 | Audit | Trail carries no clinical values | Absent | Absent | **Pass** | — |
| B-07 | Dashboard | Reports the prescription queue | Correct count | Correct | **Pass** | — |

### 5.4 Authorisation (HTTP)

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| A-01 | Authz | Anonymous → 6 back-office routes | 302 to `/login` | 302 `/login` | **Pass** | — |
| A-02 | Authz | Signed-in customer → back office | 302 to `/error/403` | 302 `/error/403` | **Pass** | — |
| A-03 | Authz | Staff → back office | 200 | 200 | **Pass** | — |
| A-04 | Authz | Staff → audit trail | Refused | Refused | **Pass** | — |
| A-05 | Authz | Administrator → audit trail | 200 | 200 | **Pass** | — |
| A-06 | Authz | Anonymous → patient/prescription export | Not 200, not `text/csv` | Refused | **Pass** | — |
| A-07 | Authz | One customer reads another's address | Refused | Refused | **Pass** | — |
| A-08 | Authz | Data export contains only own records | Own only | Own only | **Pass** | — |

### 5.5 CSRF and headers

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| S-01 | CSRF | POST upload without token | 400 | 400 | **Pass** | — |
| S-02 | CSRF | POST import without token | 400 | 400 | **Pass** | — |
| S-03 | CSRF | Every posting page renders a real token | Non-empty, > 20 chars | 155 chars | **Pass** | — |
| S-04 | CSRF | Upload with token reaches the image pipeline | Stored and listed | Stored | **Pass** | — |
| S-05 | Headers | `nosniff`, `DENY`, CSP present | All three | All three | **Pass** | — |
| S-06 | Headers | CSP names no external origin | None | None | **Pass** | — |
| S-07 | Assets | `.task` served as `application/octet-stream` | 200, correct type | 200, correct | **Pass** | — |
| S-08 | Assets | `.wasm` served as `application/wasm` | 200, correct type | 200, correct | **Pass** | — |

### 5.6 Rate limiting

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| R-01 | Limits | Repeated sign-in attempts throttled | 429 appears | 429 | **Pass** | — |
| R-02 | Limits | One visitor cannot throttle everybody | Bystander unaffected | Unaffected | **Pass** | — |
| R-03 | Limits | Failed sign-in reveals nothing about the account | Identical message | Identical | **Pass** | — |

### 5.7 Privacy

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| D-01 | Privacy | Request logged, linked and acknowledged | All three | All three | **Pass** | — |
| D-02 | Privacy | Same request cannot be opened twice | Second refused | Refused | **Pass** | — |
| D-03 | Privacy | Audit records kind, not the message | Message absent | Absent | **Pass** | — |
| D-04 | Privacy | Export carries own records only | Own only | Own only | **Pass** | — |
| D-05 | Privacy | Self-service export is audited | Entry written | Written | **Pass** | — |
| D-06 | Privacy | Erasure refused while an order is in flight | Refused | Refused | **Pass** | — |
| D-07 | Privacy | Erasure destroys identity, keeps Rx and order | Both | Both | **Pass** | — |
| D-08 | Privacy | Erasure invalidates every session | Stamp rotated | Rotated | **Pass** | — |
| D-09 | Privacy | Only an erasure request can be actioned as one | Refused | Refused | **Pass** | — |
| D-10 | Privacy | Queue lists oldest open request first | Ordered | Ordered | **Pass** | — |
| D-11 | Privacy | No patient link carries clinical data in its URL | None | None | **Pass** | — |
| D-12 | Privacy | Missing page leaks no stack trace | None | None | **Pass** | — |

### 5.8 Appointments

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| Y-01 | Diary | Booking confirmed and customer emailed | Both | Both | **Pass** | — |
| Y-02 | Diary | Same slot cannot be sold twice | Second refused | Refused | **Pass** | — |
| Y-03 | Diary | Overlapping booking refused (30 min into a 60 min slot) | Refused | Refused | **Pass** | — |
| Y-04 | Diary | Cancelling frees the slot | Rebookable | Rebookable | **Pass** | — |
| Y-05 | Diary | Nothing in the past or outside hours | Refused | Refused | **Pass** | — |
| Y-06 | Diary | Taken slot offered as unavailable, not hidden | Shown disabled | Shown | **Pass** | — |
| Y-07 | Diary | Sunday offers no slots | Empty | Empty | **Pass** | — |
| Y-08 | Diary | Cannot mark "seen" before it happened | Refused | Refused | **Pass** | — |

### 5.9 Try-on geometry (no camera required)

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| G-01 | Geometry | Anchors land exactly on the pupils | Exact | Exact | **Pass** | — |
| G-02 | Geometry | Scale is the ratio of pupil span to anchor span | Proportional | Proportional | **Pass** | — |
| G-03 | Geometry | A tilted head rotates the frame by the same angle | Equal | Equal | **Pass** | — |
| G-04 | Geometry | Default anchors match the artwork generator's contract | Match | Match | **Pass** | — |
| G-05 | Auto-fit | Frame drawn at its true manufactured width | 276/300 | 276/300 | **Pass** | — |
| G-06 | Auto-fit | A wrong PD cannot scale wildly | Clamped 0.7–1.4 | Clamped | **Pass** | — |
| G-07 | Auto-fit | Pupil sits above lens centre | Frame moves down nose | Down | **Pass** | — |
| G-08 | Pose | Straight-on face reads as neutral | ~0° all axes | Neutral | **Pass** | — |
| G-09 | Pose | Turn reported as yaw with correct sign | Signed correctly | Correct | **Pass** | — |
| G-10 | Pose | Nod reported as pitch with correct sign | Signed correctly | Correct | **Pass** | — |
| G-11 | Pose | Confidence falls with angle and with a small face | Lower | Lower | **Pass** | — |
| G-12 | Pose | Extreme angle flagged | `isExtreme` true | True | **Pass** | — |
| G-13 | Smoothing | Noise damped when still | Within ±2 of truth | Within | **Pass** | — |
| G-14 | Smoothing | Keeps up with real movement | > 250 of 290 | > 250 | **Pass** | — |
| G-15 | Smoothing | Survives repeated/backwards timestamps | No divide by zero | Finite | **Pass** | — |
| G-16 | Smoothing | Opacity falls monotonically during loss | Never rises | Never | **Pass** | — |

### 5.10 CSV

| Test ID | Module | Test case | Expected | Actual | Status | Severity |
| --- | --- | --- | --- | --- | --- | --- |
| V-01 | CSV | Quoted fields may contain commas | Parsed | Parsed | **Pass** | — |
| V-02 | CSV | Doubled quote is a literal quote | Parsed | Parsed | **Pass** | — |
| V-03 | CSV | Quoted fields may span lines | Parsed | Parsed | **Pass** | — |
| V-04 | CSV | UTF-8 BOM does not corrupt the first header | Findable | Findable | **Pass** | — |
| V-05 | Import | Dry run writes nothing | Absent afterwards | Absent | **Pass** | — |
| V-06 | Import | Money converted at the edge | Minor units stored | Correct | **Pass** | — |
| V-07 | Export | Frames export round-trips | Re-importable | Re-importable | **Pass** | — |

---

## 6. Manual verification

Performed against a running instance; not automated.

| ID | Scenario | Expected | Actual | Status |
| --- | --- | --- | --- | --- |
| MV-01 | Health endpoint | `Healthy` for database and outbox | `{"status":"Healthy",…}` | **Pass** |
| MV-02 | Six key pages load | 200 each | 200 each | **Pass** |
| MV-03 | Try-on assets serve with correct types | `.task`, `.wasm`, `.js` | All correct | **Pass** |
| MV-04 | Sign in and reach the back office | 302 then 200 | 302 → 200 | **Pass** |
| MV-05 | Media upload → WebP conversion | PNG in, WebP out | 900×600 → 5,182 B WebP | **Pass** |
| MV-06 | Media delete removes storage object | Row and file gone | Both gone | **Pass** |
| MV-07 | CSV export → edit → re-import | Stock 2 → 7 | 2 → 7 | **Pass** |
| MV-08 | Partial import failure | Good row imported, bad reported by line | `1 ok · 1 failed — line 3` | **Pass** |
| MV-09 | Frame card try-on reachable by keyboard | Tab-reachable, visible on focus | Both | **Pass** |
| MV-10 | Published artefact runs standalone | Serves correctly | Serves | **Pass** |
| MV-11 | Production guard blocks bad config | Refuses, lists all problems | Listed 3 | **Pass** |

---

## 7. Defects found and resolved

All were found during development or this review and are **fixed** in the
delivered build.

| ID | Defect | Severity | How found | Status |
| --- | --- | --- | --- | --- |
| DF-01 | Stored XSS via promotion code in the admin list | Low | This security review | **Fixed** |
| DF-02 | `[hidden]` did nothing on 12 classes; the try-on busy overlay stayed on screen permanently, washing every photo by 62 % and blurring it | High (feature-breaking) | Visual inspection of the rendered page | **Fixed** |
| DF-03 | Rate limiters shared one global bucket — 8 bad passwords locked out every customer | Medium | HTTP test harness | **Fixed** |
| DF-04 | Media uploader antiforgery token was always empty; every browser upload returned 400 | High (feature-breaking) | Manual browser test | **Fixed** |
| DF-05 | Canvas backing store fixed at 900×675 while CSS stretched it; uploaded photos were reduced then enlarged | Medium | Customer report | **Fixed** |
| DF-06 | Portrait photos letterboxed into a landscape canvas, using 56 % of the width | Medium | Customer report | **Fixed** |
| DF-07 | `.task`/`.wasm` returned 404 — the try-on silently never started | High | Manual browser test | **Fixed** |
| DF-08 | `EnableRetryOnFailure` incompatible with user transactions — HTTP 500 on order placement | High | Manual checkout | **Fixed** |
| DF-09 | Prescription used by an order could be deleted (`NoAction` allowed client-side fixup) | High | Test expected to fail but passed | **Fixed** |
| DF-10 | Integration suite drained its own seed stock; 23 tests failed after 167 accumulated orders | Medium | Test run | **Fixed** |
| DF-11 | Empty validation summary rendered as a pink error bar on all 8 forms | Low | Screenshot review | **Fixed** |
| DF-12 | ESLint walked into the .NET port, reporting 2,429 irrelevant problems | Low | Baseline run | **Fixed** |

---

## 8. Not tested

Stated explicitly rather than implied.

| Area | Reason |
| --- | --- |
| **Live camera try-on tracking** | **Not tested — reason:** requires a real camera and a human face. The geometry is covered by 66 tests without a camera, but end-to-end tracking quality was not measured. |
| **Multi-frame validation set (spec §33)** | **Not tested — reason:** requires the same. Round, square, rectangle, cat-eye, aviator, oversized and narrow frames across nine face conditions was not executed. |
| **Responsive layout at 360 / 390 / 430 px** | **Not tested — reason:** systematic breakpoint QA not completed in this cycle. |
| **Screen-reader behaviour** | **Not tested — reason:** no assistive technology available in this environment. Keyboard reachability and focus visibility were verified. |
| **Penetration test** | **Not performed — reason:** out of scope; this was a code and configuration review. |
| **IIS deployment** | **Not tested — reason:** no IIS host available. The published artefact was verified running standalone; `web.config` is verified correct by inspection only. |
| **Load / stress testing** | **Not performed** beyond the throughput benchmark in `dotnet/bench/`. |
| **Email delivery via real SMTP** | **Not tested — reason:** no SMTP server configured. Queueing and outbox draining are covered by tests. |
| **Stripe live transactions** | **Not tested — reason:** no live keys. Provider registration and safe degradation are covered. |
| **Rate-limit window expiry** | **Not tested — reason:** verified to engage and partition; a real five-minute expiry was not waited out. |

---

## 9. Recommendations

| Priority | Recommendation |
| --- | --- |
| High | Complete a live try-on session against real faces and the multi-frame validation set before customer release |
| High | Deploy to a staging IIS host and re-run the verification checklist in `09_DEPLOYMENT_DOCUMENT.md` §6 |
| Medium | Constrain promotion codes to `[A-Z0-9-]` on save (§9.1 of the security document) |
| Medium | Add `dotnet list package --vulnerable` to CI |
| Medium | Systematic responsive QA at 360 / 390 / 430 px |
| Low | Remove remaining inline styles and drop `'unsafe-inline'` from `style-src` |
| Low | Screen-reader pass with NVDA or JAWS |

---

## 10. Overall assessment

**298 of 298 automated tests pass.** Coverage is strongest where the cost of
being wrong is highest: money handling, prescription validation, authorisation,
data-subject rights and try-on geometry.

The suite has repeatedly caught real defects — including three that a green
service-level suite could not see, which is why the HTTP layer exists. Twelve
defects were found and fixed rather than deferred.

The principal gap is that **live camera tracking has never been measured against
a real face**. The geometry beneath it is well covered, but that is not the same
claim, and it should be closed before customer release.

---

*Cross-references: security findings in `03_SECURITY_FEATURES_DOCUMENT.md`;
deployment verification in `09_DEPLOYMENT_DOCUMENT.md`.*
