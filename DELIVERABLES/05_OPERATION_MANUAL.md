# VisionCart — Operation Manual

**Prepared** 25 August 2026
**Audience** Shop staff, opticians, and the administrator
**Companion** `USER-MANUAL.pdf` — a 57-page illustrated manual with 29 screenshots, covering the customer side as well

> This document is the operational reference. For a screenshot-led walkthrough
> aimed at somebody using the shop for the first time, use `USER-MANUAL.pdf`.

---

## 1. System overview

VisionCart is an online optician. Customers browse frames, try them on with
their camera, enter a prescription, choose lenses and order. Staff process those
orders, an optician verifies every prescription, and the practice runs its
appointment diary in the same system.

The important difference from ordinary retail: **the clinical record is a
primary entity.** Every customer — including guests — has a patient file,
because an optical order cannot be remade or followed up without one.

---

## 2. Signing in

Go to your shop's address followed by `/admin`, or sign in normally and you will
be taken there.

| Field | Notes |
| --- | --- |
| Email | Your work address |
| Password | Minimum 8 characters, at least one digit and one lowercase letter |

**Five failed attempts locks the account for 15 minutes.** Use *Forgotten your
password?* rather than guessing; the reset link works once and expires after six
hours.

Changing your password **signs you out everywhere else** — the quickest way to
be certain nobody is still signed in as you on a shared machine.

---

## 3. Roles

| Role | Can do |
| --- | --- |
| **Customer** | Shop, manage their own account. No back-office access at all. |
| **Staff** | Orders, patients, diary, catalogue, media, offers, delivery, import/export, data requests |
| **Optician** | Everything Staff can do, **plus verifying and rejecting prescriptions** |
| **Administrator** | Everything, plus settings, the audit trail and data erasure |

If a menu item described here is missing, your account does not have that role.

> **Never share an account.** Every change is recorded against whoever was signed
> in, and a shared login makes that record worthless exactly when you need it.

---

## 4. The dashboard

The first screen, designed to answer one question: *what needs attention today?*

| Tile | Meaning |
| --- | --- |
| Orders today | Placed since midnight |
| Paid, last 30 days | Money actually received |
| **Awaiting payment** | Placed but not yet paid |
| In the lab | With the laboratory now |
| Patient files | People on record |
| Live frames | Currently on sale |
| **Prescriptions to check** | Waiting for an optician |
| **Low stock lines** | Colourways nearly sold out |

**Amber tiles are work. The rest are information.**

Below them sits the prescription queue, oldest first. This is the most important
list in the shop — nothing can be made until an optician has been through it.

---

## 5. Navigation

The back-office bar carries: Dashboard · Orders · Patients · Diary · Frames ·
Lenses · Media · Promotions · Delivery · Import · Data requests · Audit
(administrators) · Settings.

*View shop ↗* opens the customer-facing site in a new tab.

---

## 6. Orders

### 6.1 Finding one

Search by order number, email or telephone. Three drop-downs narrow by status,
payment and lab stage.

### 6.2 The life of an order

| Stage | Meaning |
| --- | --- |
| Pending | Placed, not yet paid |
| Paid | Payment received |
| In lab | Being made |
| Ready | Made, awaiting dispatch or collection |
| Shipped | On its way |
| Delivered | Complete |
| Cancelled | Stopped — stock returns to the shelf |
| Refunded | Money returned |

Payment status and progress are tracked **separately**, because an order can be
paid but unmade, or made but unpaid.

### 6.3 Taking a payment by hand

For cash on delivery or bank transfer: open the order → **Mark as paid** → add a
reference such as the transfer number.

This does exactly what an automatic card payment does — same record, same email
to the customer. There is no second, weaker path.

### 6.4 Dispatching

Open the order → **Mark as shipped** → choose the courier and enter the tracking
number. The customer is emailed automatically.

### 6.5 Cancelling

Set **Status** to *Cancelled* and click **Update**, noting the reason in the
internal notes.

> **Take care.** Cancelling returns frames to stock. If lenses have already been
> made to a prescription they cannot be sold to anybody else — check with the lab
> before cancelling anything past *In lab*.

---

## 7. Prescriptions — opticians

Every prescription arrives **pending verification**, from any source including
spreadsheet import. Nothing is made until an optician has looked at it.

### 7.1 Working the queue

From the dashboard, click any patient in *Prescriptions waiting for an optician*,
or open a file from **Patients**.

Check the values, then:

- **Verify** — the order moves to the lab and the customer is emailed
- **Send & reject** — give a reason; the customer is emailed that reason

Write rejections as something a customer can act on: *"the axis is missing for
the right eye"*, not *"invalid"*.

### 7.2 Why a prescription cannot be edited

Once used by an order it is **locked**. Changes create a new version; the old one
stays exactly as it was.

This is not awkwardness. If a customer reports a problem with their glasses, the
practice must be able to show precisely what was made and what it was made from.
A record that can be quietly edited afterwards proves nothing.

### 7.3 Values the system will not accept

Enforced identically on the customer form, your screen and spreadsheet import:

- Every value must sit on a **0.25 D step**
- A **cylinder must have an axis**

Both exist because a lab cannot make anything else.

---

## 8. Patients

Everyone who has ever ordered has a file, guests included. Search by name, file
number (`P-000042`), email or telephone.

A file holds contact details, every prescription, orders, appointments and
documents. **Add prescription** creates a new version — it never overwrites.

> **Take care.** Patient files contain health information. Do not leave one open
> on a screen the public can see, and never email a screenshot of one.

---

## 9. The clinic diary

Move through the calendar with **Earlier**, **Today** and **Later**, and change
how many days are shown.

### 9.1 During the day

Each booking carries three buttons: **Seen**, **No show**, **Cancel**.

**Seen** only becomes available once the appointment time has passed. If you
cannot click it, check you are not looking at tomorrow.

### 9.2 Booking by telephone

Use the booking form on the diary page: patient, date and time, type, length.

The diary **will not let you double-book**. If two bookings would overlap for the
same clinician, the second is refused with an explanation. Opening hours are
10:00–18:00, Monday to Saturday; Sunday offers no slots.

---

## 10. Catalogue

### 10.1 Frames

**New frame** creates one.

| Field | Notes |
| --- | --- |
| Name and SKU | The SKU is your product code and must be unique |
| Brand | Created automatically if new |
| Price | In rupees, as on a price list — the system converts and stores it in a way that cannot drift |
| Shape, material, rim | Drive the customer-facing filters |
| Measurements | Lens width, bridge, arm length in millimetres |
| Status | *Draft* invisible · *Active* on sale · *Archived* withdrawn but kept for old orders |

Each frame has colourways, each with its own product code, stock level and
photographs — so you can sell out of black without affecting tortoiseshell.

### 10.2 Lenses

**Add an option** creates one. Each has a code, name, price and position within
its group. Retiring an option hides it from customers immediately but leaves past
orders untouched.

---

## 11. Media

Drag a whole shoot onto the drop area, or click to choose. Each photograph is
auto-rotated from its EXIF orientation, capped at 2000 px, converted to WebP and
thumbnailed.

Files upload **one at a time**, so a single corrupt image is reported by name
while the rest of the shoot goes through.

**Try-on artwork** must be a PNG with a transparent background — tick *Keep
transparency* before uploading, or the frame appears with a white box around it.

> An image attached to a colourway **cannot** be deleted; the system refuses
> rather than leaving a product page with a broken picture. Detach it first.

---

## 12. Promotions

**New deal** creates one. Types: percentage off, fixed amount off, free delivery,
buy-one-get-one.

| Setting | Effect |
| --- | --- |
| Code | Blank means it applies automatically |
| Minimum spend | Below this it does not apply — and the customer is told why |
| Maximum discount | Caps the amount |
| Can be combined | Whether it stacks |
| Priority | Which offer wins when several could apply |
| Starts / ends | Blank runs indefinitely |

> An offer with no minimum, no cap and no end date is how shops lose money. Set
> at least an end date.

---

## 13. Delivery

**Add a delivery rate** creates one: a name the customer sees, the area it covers,
a price and an estimated number of days. Set the price to zero for free delivery.

---

## 14. Import and export

### 14.1 Exporting

Four exports: frames & stock, patients, prescriptions, orders. Each downloads as
a spreadsheet.

> **Take care.** Patient and prescription exports contain health information.
> Every download is recorded in the audit log. Do not email these files or leave
> them in a shared folder.

### 14.2 Importing

1. Export the matching file first — it already has the right columns
2. Edit it in Excel
3. Choose the file, click **Check the file**
4. Read the report. **Nothing has been written yet**
5. If it looks right, click **Import for real**

The check lists problems by **the line number you see in Excel**, so you can jump
straight to the row.

Frames are matched on colourway product code, patients on file number. A matching
row updates; a new one creates. **A bad row does not stop the good ones** — ten
good rows import and the eleventh is reported.

An imported prescription always arrives *pending verification*; it never goes
straight to the lab.

---

## 15. Data requests

The queue lists requests oldest first — a legal clock runs on each.

Open one, confirm who the person is, do what they asked, set the status to
*Completed* and note what you did and how you confirmed their identity.

> **Erasure is permanent and administrator-only.** It removes everything
> identifying the customer across their file, orders and addresses, and closes
> their account. Prescriptions and order totals are kept — both have retention
> obligations — but nothing left behind identifies a person.
>
> You must type `ERASE` to confirm. The system refuses entirely while the
> customer has an order still in flight: the courier needs somewhere to deliver.

---

## 16. Settings — administrators

Shop name, currency, tax rate, contact details, payment methods offered, bank
transfer instructions.

> Changing the tax rate affects every **new** order immediately. It does not
> change orders already placed — those keep the figures they were sold at, which
> is what an accountant needs.

---

## 17. The audit trail — administrators

Every change to a patient record, price or order: who, what, when, and from which
computer.

Used for answering *"who changed this price?"*, showing a regulator that patient
records are controlled, and investigating a mistake without guesswork.

The trail deliberately records **that** a prescription changed, never the values.
Clinical detail lives on the patient file where access is controlled — not in a
log staff browse casually.

---

## 18. Routine operation

### Daily
- Clear the **Prescriptions to check** queue — nothing gets made until you do
- Check **Awaiting payment** for stalled orders
- Look at tomorrow's diary

### Weekly
- Review **Low stock lines** and reorder
- Check the data-request queue — the legal clock does not stop for a busy week
- Glance at the audit trail for anything unexpected

### Monthly
- Export orders for your accountant
- Review which offers are still running
- **Confirm your backups actually exist**

---

## 19. Is the shop healthy?

Two addresses answer this without needing anyone technical:

| Address | Question |
| --- | --- |
| `/health/live` | Is the shop switched on? |
| `/health/ready` | Can it actually take an order? |

**The second is the one that matters.** A shop can be switched on and still be
unable to reach its records, and from the outside those look identical.

A healthy response:

```json
{"status":"Healthy","checks":{"database":"Healthy","email-outbox":"Healthy"}}
```

`Degraded` on `email-outbox` means mail is backing up — customers are not
receiving confirmations.

---

## 20. Troubleshooting

| Problem | What to do |
| --- | --- |
| A menu item is missing | Your account lacks that role. Ask an administrator. |
| "That slot has just been taken" | Somebody booked it while you typed. Choose another time. |
| An import failed | Read the line numbers in the report and fix those rows in Excel. |
| An image will not delete | It is attached to a colourway. Detach it first. |
| Lenses cannot be marked ready | The prescription has not been verified by an optician. |
| Too many sign-in attempts | Sign-in is rate limited to protect accounts. Wait five minutes. |
| Account locked | Five failed attempts locks it for 15 minutes. |
| Customer says no confirmation email | Check `/health/ready`. If `email-outbox` is *Degraded*, mail is failing. |
| The site is slow on the first visit each morning | Shared hosting stops the app when idle; the first request restarts it. Normal. |
| Site will not start after an update | See `09_DEPLOYMENT_DOCUMENT.md` §7 — the startup guard reports exactly what is wrong. |

### 20.1 Reading the logs

`logs/visioncart-YYYY-MM-DD.log`. Files roll daily and at 20 MB; anything older
than 14 days is deleted. Not reachable over HTTP.

If the site will not start at all, the application never gets far enough to log —
see the deployment document for the temporary stdout channel.

---

*Cross-references: illustrated walkthrough in `USER-MANUAL.pdf`; features in
`02_FEATURES_DOCUMENT.md`; deployment in `09_DEPLOYMENT_DOCUMENT.md`.*
