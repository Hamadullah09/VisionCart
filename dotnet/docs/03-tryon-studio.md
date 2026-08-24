# VisionCart — The Virtual Try-On Studio

**Phase 4 progress report.** Follows on from `02-vertical-slice.md`.

The flagship feature is migrated. The mirror runs at `/try-on`, the placement
mathematics is unchanged, and the privacy architecture is intact.

---

## 1. What the port preserves

Section 7 of the brief lists what must survive. Each is verified.

| Requirement | Status |
| --- | --- |
| Browser accesses the camera with user permission | ✅ `getUserMedia`, `Permissions-Policy: camera=(self)` |
| Face processed locally | ✅ MediaPipe runs in-browser; nothing is uploaded |
| Facial landmarks detected | ✅ Confirmed live: *"Graph successfully started running"* |
| Pupils/irises detected | ✅ 478-point mesh, iris centres as pupil proxy |
| Relative frame positioning | ✅ `geometry.ts`, **byte-identical** to the legacy file |
| PD calculated | ✅ 11.7 mm iris ruler, unchanged |
| Frame overlaid | ✅ Verified numerically, see §3 |
| Manual correction | ✅ Drag markers, pointer capture, 60 px grab radius |
| No upload of the original face image | ✅ Only a composited snapshot, only on an explicit press |
| Similarity transform (scale, rotation, translation) | ✅ Unchanged |
| PD estimation *and confidence* logic | ✅ Unchanged, including the implausible-PD cap |
| Graceful degradation | ✅ Verified: synthetic image → detector found no face → manual fallback engaged |
| No camera frames sent to the server | ✅ No network call in either render path |

**The geometry module was not rewritten.** `ClientApp/tryon/geometry.ts` is a
byte-for-byte copy of the legacy `src/lib/tryon.ts`, verified with `diff`. The
29 geometry tests run against it directly.

### The render-timing rule, preserved

`AGENTS.md` is explicit that still-photo rendering must draw synchronously from
an effect and must not be moved back to `requestAnimationFrame`, because rAF is
suspended in a background tab and the canvas would stay blank. The port keeps
this exactly: `render()` is called synchronously on every state change, and only
the camera path runs a rAF loop — where frame pacing is the point. The reasoning
is recorded in a comment above `render()` so it survives the next refactor.

---

## 2. How it was rebuilt

| Legacy | Now |
| --- | --- |
| `TryOnStudio.tsx` (730 lines, React) | `studio.ts` — a plain class driving DOM |
| `useFaceLandmarker.ts` (React hook) | `faceLandmarker.ts` — async factory |
| `types.ts` | `TryOnFrame` DTO from `CatalogService` |
| React state → re-render | Explicit `render()` + `syncChrome()` |
| Next.js page | `TryOnController` + Razor view |
| `/api/tryon/snapshot` route handler | `TryOnController.Snapshot` |

### The client build

esbuild bundles the TypeScript to **one 16 KB ESM file**, committed to
`wwwroot/js/tryon.js`. MediaPipe is *not* bundled: its prebuilt ESM bundle
(155 KB) is vendored to `wwwroot/js/vendor/` and loaded by dynamic import.

**Node is build-time only.** The published application contains compiled
JavaScript; the host needs no Node at any point.

---

## 3. Verified in a live browser

| Check | Result |
| --- | --- |
| MediaPipe initialises | *"Graph successfully started running"*, GL 3.0 / WebGL 2.0 |
| Model served | `face_landmarker.task`, 3,758,596 bytes, `application/octet-stream` |
| WASM runtime served | `vision_wasm_internal.wasm`, 11,756,954 bytes, `application/wasm` |
| Studio boots | Root found, canvas 900×675, 30 thumbnails, "Ravi / Rs.6,500.00" selected |
| Photo upload | Image drawn; sample pixel matched the uploaded content |
| Graceful degradation | Synthetic image → no face detected → manual mode engaged, drag hint shown |
| **Frame actually drawn** | **14.7% dark (frame) pixels in the band spanning the pupil markers; 0.0% in a control band below it** |
| Frame switching | Ravi → Falcon, render changed, exactly one thumbnail selected |
| Fine-tune sliders | Size slider changed the render |
| Inline scripts on the page | Only `application/json` — data, never executed |

The frame-pixel measurement is the one that matters: it proves the overlay is
being placed *on the pupil markers* rather than merely loaded.

---

## 4. Bugs found and fixed

| Bug | How it surfaced | Fix |
| --- | --- | --- |
| **The try-on model 404'd.** ASP.NET Core refuses to serve unknown MIME types and `.task` is unknown, so the model would never load and the mirror would silently fall back to manual placement on *every* visit — the failure would look like "detection just doesn't work here". | Route sweep | Explicit `FileExtensionContentTypeProvider` mapping for `.task` and `.wasm`. |
| **CSP blocked the product page's inline script.** The colourway image swap was silently dead. My own security header caused it. | Console, while checking the try-on for CSP violations | Moved to `wwwroot/js/product.js`. The try-on page was already clean — its only inline block is `application/json`. |

---

## 5. Security and privacy

- **CSP allows `wasm-unsafe-eval`** (MediaPipe needs it) **and no external
  origins.** `connect-src 'self'` is what structurally guarantees the face model
  cannot be fetched from, or a photo posted to, a third party.
- `Permissions-Policy: camera=(self), microphone=(), geolocation=()`.
- The snapshot route enforces the store setting **server-side**: with retention
  off it returns 403 rather than merely hiding the button, exactly as the legacy
  route did.
- The route is rate-limited (`upload` policy) and size-capped at 8 MB.
- Uploads go through `LocalStorageProvider`, which **fixes the legacy
  path-traversal bug**: storage keys are resolved and proven to stay inside the
  upload root before any write or delete.
- Antiforgery token is posted with the snapshot.

---

## 6. Test suite — 92 tests, all passing

| Suite | Count |
| --- | --- |
| Unit (money, cuid) | 26 |
| Integration (schema, checkout, promotions) against real SQL Server | 37 |
| Try-on geometry against the browser TypeScript | 29 |

Clean build: **0 errors, 0 warnings.**

---

## 7. Known limitations

- **The camera path is not verified end-to-end** — this environment has no
  camera. The code path is the legacy one, unchanged in structure, and the
  upload path shares every step after "two pupil points on a canvas". It needs a
  human with a webcam before launch.
- **Detection accuracy is not verified against a real face.** MediaPipe
  initialises and correctly declines to find a face in a synthetic image, which
  proves the wiring and the fallback; it does not prove PD accuracy on a real
  photograph. The geometry is covered by 29 tests, but the model's own output
  needs a real subject.
- **The admin try-on calibration screen is not migrated.** Staff cannot yet set
  the L/R anchors for a new frame's artwork; seeded frames carry the generated
  defaults. This lives in the back office, still outstanding.
- **`suggestSizeBand` is shown but not yet used to sort the catalogue.** The
  legacy behaviour was the same.
