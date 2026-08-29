# VisionCart — Customer Delivery Document

**Delivered** 25 August 2026
**Product** VisionCart Optical — prescription eyewear shop with virtual try-on

---

## 1. What you have received

| # | Item | Where |
| --- | --- | --- |
| 1 | Complete source code | `SOURCE_CODE_PACKAGE.zip` |
| 2 | Database schema and reference | `database/` |
| 3 | Illustrated user manual, 57 pages | `USER-MANUAL.pdf` |
| 4 | Screenshots of every screen | `screenshots/` — 29 images |
| 5 | Ten documentation deliverables | this folder |

Everything needed to run, maintain, extend and deploy the application is
included. No component is withheld, and no licence must be purchased to build or
run it.

---

## 2. What the software does

VisionCart is an **online optician**, not a general shop that happens to sell
glasses. That distinction shapes the whole product.

### For your customers

| | |
| --- | --- |
| **Browse** | Filter frames by shape, material, rim, wearer, brand, collection, size and price |
| **Try on** | See any frame on their own face using their camera or an uploaded photo |
| **Measure** | Their pupillary distance is estimated while they browse |
| **Prescribe** | Enter a prescription with guided, error-proof inputs |
| **Choose lenses** | Type, thickness, coatings, tints |
| **Order** | Cash on delivery, bank transfer or card |
| **Manage** | Order history, saved addresses, appointments, their own data |

### For your staff

| | |
| --- | --- |
| **Dashboard** | What needs attention today, at a glance |
| **Orders** | Payment, lab progress, dispatch, cancellation |
| **Prescriptions** | An optician verifies every one before anything is made |
| **Patients** | The clinical record for each customer |
| **Diary** | Appointments with no double-booking possible |
| **Catalogue** | Frames, colourways, stock, lens options |
| **Media** | Bulk photograph upload with automatic processing |
| **Offers** | Discount codes and automatic promotions |
| **Spreadsheets** | Import and export with a safe dry run |
| **Data requests** | Correction, export and erasure |
| **Audit** | Who changed what, and when |

---

## 3. The virtual try-on

This is the feature that distinguishes the shop, and the one with the strongest
privacy guarantee.

**Your customer's face never leaves their device.**

Everything — finding the eyes, measuring the pupillary distance, placing the
frame — happens inside their own browser. The face model is served from your own
website rather than a third-party service, specifically so that stays true. A
photograph reaches your server **only** if the customer chooses to save one to
their file.

The system tracks head **tilt, turn and nod**, sizes each frame to its true
manufactured width against the measured face, and reports whether the frame
actually suits them:

> **Good fit · 99% of face width**
> Frame width 138 mm · +1.8 mm on the nose · 2.3° head tilt, corrected

Where a frame does not suit, it says so plainly — *"This frame is 12% wider than
the face — it will slide down."*

### An honest limitation

The pupillary distance is an **estimate**, and the software describes it as one
throughout. It is accurate to roughly ±2 mm and your optician confirms it before
lenses are cut. The software makes no claim of medical-grade accuracy, and we
would not recommend adding one.

---

## 4. Screenshots

All 29 are in `screenshots/`, captured from the running application.

### Customer-facing

| File | Screen |
| --- | --- |
| `01-home.png` | Home |
| `02-catalogue.png` | Frame catalogue with filters |
| `03-product.png` | Frame detail |
| `04-tryon.png` | Virtual try-on |
| `05-cart.png` | Bag |
| `06-signin.png` | Sign in |
| `07-register.png` | Create account |
| `08-guide-prescription.png` | Prescription guide |
| `09-data-request.png` | Data request form |
| `10-account.png` | Account |
| `11-addresses.png` | Address book |
| `12-address-form.png` | Add an address |
| `13-appointments.png` | Appointments |
| `14-book-appointment.png` | Booking |
| `15-your-data.png` | Your data |

### Back office

| File | Screen |
| --- | --- |
| `20-dashboard.png` | Dashboard |
| `21-orders.png` | Orders |
| `22-patients.png` | Patients |
| `23-diary.png` | Clinic diary |
| `24-frames.png` | Frames |
| `25-frame-form.png` | Frame editor |
| `26-lenses.png` | Lens options |
| `27-media.png` | Media library |
| `28-promotions.png` | Promotions |
| `29-delivery.png` | Delivery rates |
| `30-import-export.png` | Import and export |
| `31-data-requests.png` | Data requests |
| `32-audit.png` | Audit trail |
| `33-settings.png` | Settings |

---

## 5. Key workflows

### 5.1 A customer buys glasses

```
  Home ──► Browse frames ──► Open a frame
                                  │
                                  ▼
                          Try it on (camera or photo)
                                  │
                          Fit report: size, height, tilt
                                  │
                                  ▼
                      Choose lens type, thickness, coatings
                                  │
                                  ▼
                      Enter prescription (guided dropdowns)
                                  │
                                  ▼
                          Bag ──► Checkout ──► Order placed
                                                    │
                                            Confirmation email
```

*Screens: `01-home` → `02-catalogue` → `03-product` → `04-tryon` → `05-cart`*

### 5.2 Your shop fulfils it

```
  Order arrives
        │
        ▼
  Optician checks the prescription  ──► rejected: customer emailed the reason
        │ verified
        ▼
  Payment recorded (automatic, or "Mark as paid")
        │
        ▼
  Sent to the lab ──► Ready ──► "Mark as shipped" + tracking
        │
        ▼
  Customer emailed at each step
```

*Screens: `20-dashboard` → `21-orders` → `22-patients`*

### 5.3 A customer books an eye test

```
  Account ──► Appointments ──► Book
                                 │
                    Choose a date; taken times shown struck through
                                 │
                                 ▼
                        Booked, confirmation emailed
                                 │
                                 ▼
              Appears in your diary; mark Seen / No show / Cancel
```

*Screens: `13-appointments` → `14-book-appointment` → `23-diary`*

### 5.4 A customer asks about their data

```
  Any visitor ──► "Your data" ──► Download a copy (immediate)
                        │
                        └──► Make a request: correct · restrict · erase
                                        │
                                        ▼
                        Appears in your queue, oldest first
                                        │
                                        ▼
                        Confirm identity, act, mark completed
```

*Screens: `15-your-data` → `09-data-request` → `31-data-requests`*

---

## 6. Getting started

### First run, locally

```bash
cd dotnet
cp src/VisionCart.Web/appsettings.Development.example.json \
   src/VisionCart.Web/appsettings.Development.json
dotnet run --project src/VisionCart.Web
```

The shop is then at `http://localhost:5217`, and the back office at `/admin`. The
database creates and seeds itself on first start.

### Going live

Follow `09_DEPLOYMENT_DOCUMENT.md`. Eight items need your input — connection
string, domain, email settings and the first administrator account among them;
they are collected in §11 of that document.

**There are no default passwords.** You create the first administrator
deliberately, once, and the software refuses to start if it detects a demo
password.

---

## 7. What is included, and what is not

### Included

- All source code, buildable with the free .NET SDK
- Complete database schema, plus documentation of every table, column, key and index
- 298 automated tests, all passing
- Ten documentation deliverables
- 57-page illustrated user manual
- Deployment procedure and rollback plan
- Frame artwork generator and the try-on asset fetcher

### Not included, and why

| Item | Note |
| --- | --- |
| Hosting | You provide IIS and SQL Server |
| SMTP account | You provide the mail service |
| Stripe account | Only if you want card payments |
| TLS certificate | Provided by your host |
| Production data | The shop seeds sample frames; your real catalogue is yours to load |

### No paid licences

Every dependency is MIT or Apache 2.0. Two candidate libraries were **rejected
during development specifically because their newer versions require a paid
commercial licence** — ImageSharp and FluentAssertions. Nothing here will present
you with a bill.

---

## 8. Known limitations

Stated plainly, because you should hear them from us rather than discover them.

| Limitation | Detail |
| --- | --- |
| **Live try-on tracking is unmeasured** | The geometry is covered by 66 automated tests, but tracking quality against real faces has not been measured. We recommend a session with several people and several frame shapes before launch. |
| **Responsive layout not systematically tested** | Layouts use responsive CSS and behave correctly in testing, but a formal pass at 360/390/430 px was not completed. |
| **No deployment to a real IIS host yet** | The published package was verified running standalone; the IIS configuration is verified by inspection. |
| **Try-on calibration has no visual admin screen** | Frame anchors are data. Adding a new frame with try-on artwork currently needs the anchor values set directly. |
| **Extreme head angles** | Beyond about 28°, a flat overlay cannot convince — the far lens would need to be hidden by the cheek. The software detects this and asks the customer to turn towards the camera rather than drawing something visibly wrong. |
| **Single currency and language** | English, one currency from settings. |
| **No penetration test** | A code and configuration security review was performed; an external test was not. |

---

## 9. Support and handover

| Question | Where |
| --- | --- |
| How do I use it? | `USER-MANUAL.pdf`, or `05_OPERATION_MANUAL.md` |
| How do I deploy it? | `09_DEPLOYMENT_DOCUMENT.md` |
| What was built? | `02_FEATURES_DOCUMENT.md` |
| Is it secure? | `03_SECURITY_FEATURES_DOCUMENT.md` |
| Was it tested? | `04_TEST_REPORT.md` |
| How is it built? | `07_APPLICATION_ARCHITECTURE.md` |
| What is in the database? | `08_DATABASE_DOCUMENTATION.md` |
| What was originally asked for? | `01_MASTER_PROJECT_PROMPT.md` |

For a developer taking this on, `AGENTS.md` in the source root carries the
engineering conventions — the rules that must not be broken and the reason each
one exists.

---

## 10. Acceptance checklist

Suggested before sign-off:

- [ ] Application runs locally following §6
- [ ] Signed in to the back office and reviewed each screen against `screenshots/`
- [ ] Placed a test order end to end
- [ ] Verified a prescription as an optician
- [ ] Booked and cancelled an appointment
- [ ] Uploaded a photograph to the media library
- [ ] Exported and re-imported a spreadsheet
- [ ] Tried the virtual try-on with a real camera
- [ ] Reviewed the known limitations in §8
- [ ] Confirmed backups cover **both** database and `wwwroot/uploads`

---

*Prepared 25 August 2026. All statements verified against the delivered source.*
