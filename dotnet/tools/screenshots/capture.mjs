/**
 * Captures the screenshots used in the user manual.
 *
 * Documentation tooling. It drives the installed Chrome through playwright-core
 * rather than downloading a browser, so it adds nothing to the deployment and
 * needs no network.
 *
 * Run the application first, then:
 *   npm run capture
 *
 * Every shot is deterministic: fixed viewport, a real sign-in for staff pages,
 * and animations disabled — a manual whose figures shift between runs is a
 * manual nobody trusts.
 */
import { chromium } from "playwright-core";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.join(HERE, "..", "..", "docs", "screenshots");

const BASE = process.env.VISIONCART_URL ?? "http://localhost:5217";
const STAFF_EMAIL = process.env.VISIONCART_EMAIL ?? "admin@visioncart.local";

// Deliberately no default. A working password living in a file that goes to
// the remote is exactly what appsettings.Development.json is kept out of the
// repository to avoid.
const STAFF_PASSWORD = process.env.VISIONCART_PASSWORD;
if (!STAFF_PASSWORD) {
  console.error(
    "\nSet VISIONCART_PASSWORD to the staff password from your "
    + "appsettings.Development.json:\n"
    + "  VISIONCART_PASSWORD=... npm run capture\n");
  process.exit(1);
}

/** Wide enough for the back office's tables without a horizontal scrollbar. */
const VIEWPORT = { width: 1360, height: 900 };

/**
 * `full: true` captures the whole scrollable page. Use it for anything a reader
 * needs to see in one piece; leave it off where a long page would shrink the
 * figure to illegibility in print.
 */
const PUBLIC_SHOTS = [
  { file: "01-home", url: "/", full: false },
  { file: "02-catalogue", url: "/frames", full: false },
  { file: "03-product", url: null, full: false },
  { file: "04-tryon", url: "/try-on", full: false },
  { file: "05-cart", url: "/cart", full: false },
  { file: "06-signin", url: "/login", full: false },
  { file: "07-register", url: "/register", full: false },
  { file: "08-guide-prescription", url: "/guides/prescription", full: false },
  { file: "09-data-request", url: "/account/privacy/request", full: false },
];

const CUSTOMER_SHOTS = [
  { file: "10-account", url: "/account", full: false },
  { file: "11-addresses", url: "/account/addresses", full: false },
  { file: "12-address-form", url: "/account/addresses/new", full: false },
  { file: "13-appointments", url: "/account/appointments", full: false },
  { file: "14-book-appointment", url: "/account/appointments/book", full: false },
  { file: "15-your-data", url: "/account/privacy", full: false },
];

const STAFF_SHOTS = [
  { file: "20-dashboard", url: "/admin", full: false },
  { file: "21-orders", url: "/admin/orders", full: false },
  { file: "22-patients", url: "/admin/patients", full: false },
  { file: "23-diary", url: "/admin/diary", full: false },
  { file: "24-frames", url: "/admin/frames", full: false },
  { file: "25-frame-form", url: "/admin/frames/new", full: false },
  { file: "26-lenses", url: "/admin/lenses", full: false },
  { file: "27-media", url: "/admin/media", full: false },
  { file: "28-promotions", url: "/admin/promotions", full: false },
  { file: "29-delivery", url: "/admin/shipping", full: false },
  { file: "30-import-export", url: "/admin/import", full: false },
  { file: "31-data-requests", url: "/admin/data-requests", full: false },
  { file: "32-audit", url: "/admin/audit", full: false },
  { file: "33-settings", url: "/admin/settings", full: false },
];

async function shoot(page, { file, url, full }) {
  if (url) {
    const response = await page.goto(BASE + url, { waitUntil: "networkidle" });
    if (response && response.status() >= 400) {
      console.log(`  !  ${file}  HTTP ${response.status()} at ${url} — skipped`);
      return false;
    }
  }

  // Fonts settle after networkidle; without this the first shot of a run can
  // catch a fallback face and look different from every other figure.
  await page.waitForTimeout(350);

  await page.screenshot({
    path: path.join(OUT, `${file}.png`),
    fullPage: Boolean(full),
  });

  console.log(`  ok ${file}.png`);
  return true;
}

async function main() {
  await mkdir(OUT, { recursive: true });

  const browser = await chromium.launch({ channel: "chrome", headless: true });
  const context = await browser.newContext({
    viewport: VIEWPORT,
    deviceScaleFactor: 2, // Retina density, so the figures stay sharp in print.
    reducedMotion: "reduce",
    colorScheme: "light",
  });

  // A caret blinking in a focused field, or a hover state left over from the
  // previous shot, makes two otherwise identical figures differ.
  await context.addInitScript(() => {
    const style = document.createElement("style");
    style.textContent =
      "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}";
    document.documentElement.appendChild(style);
  });

  const page = await context.newPage();
  let taken = 0;

  console.log("\nPublic pages");
  for (const shot of PUBLIC_SHOTS) {
    if (shot.file === "03-product") {
      // Pick a real product rather than hardcoding a slug the seed may rename.
      await page.goto(BASE + "/frames", { waitUntil: "networkidle" });
      const href = await page.locator('a[href^="/frames/"]').first().getAttribute("href");
      if (!href) { console.log("  !  03-product  no product link found — skipped"); continue; }
      await page.goto(BASE + href, { waitUntil: "networkidle" });
    }
    if (await shoot(page, shot)) taken++;
  }

  console.log("\nSigning in");
  await page.goto(BASE + "/login", { waitUntil: "networkidle" });
  await page.fill('input[name="Email"]', STAFF_EMAIL);
  await page.fill('input[name="Password"]', STAFF_PASSWORD);
  await page.click('button[type="submit"]');
  await page.waitForLoadState("networkidle");

  if (page.url().includes("/login")) {
    throw new Error("Sign-in failed. Check VISIONCART_EMAIL / VISIONCART_PASSWORD.");
  }
  console.log("  ok signed in");

  console.log("\nCustomer account");
  for (const shot of CUSTOMER_SHOTS) if (await shoot(page, shot)) taken++;

  console.log("\nBack office");
  for (const shot of STAFF_SHOTS) if (await shoot(page, shot)) taken++;

  await browser.close();
  console.log(`\n${taken} screenshots written to docs/screenshots\n`);
}

main().catch((error) => {
  console.error("\ncapture failed:", error.message, "\n");
  process.exit(1);
});
