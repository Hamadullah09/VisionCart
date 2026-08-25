/**
 * Records the VisionCart user-guide videos.
 *
 * Run the application first, then:
 *   VISIONCART_PASSWORD=... node record.mjs            (all five)
 *   VISIONCART_PASSWORD=... node record.mjs 3          (just scene 3)
 *
 * Output lands in docs/videoguide/ as WebM, which plays in every current
 * browser. There is no soundtrack — everything is captioned on screen, because
 * a guide that needs headphones is a guide half the office cannot watch.
 */
import { chromium } from "playwright-core";
import { mkdir, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { OVERLAY, FURNITURE, narrator } from "./narrate.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.join(HERE, "..", "..", "docs", "videoguide");
const TMP = path.join(HERE, ".recordings");

const BASE = process.env.VISIONCART_URL ?? "http://localhost:5217";
const STAFF_EMAIL = process.env.VISIONCART_EMAIL ?? "admin@visioncart.local";
const STAFF_PASSWORD = process.env.VISIONCART_PASSWORD;

if (!STAFF_PASSWORD) {
  console.error(
    "\nSet VISIONCART_PASSWORD to the staff password from your "
    + "appsettings.Development.json:\n"
    + "  VISIONCART_PASSWORD=... node record.mjs\n");
  process.exit(1);
}

const SIZE = { width: 1440, height: 900 };

// ---------------------------------------------------------------------------
// Scenes
// ---------------------------------------------------------------------------

async function buyingGlasses(page, n) {
  await n.titleCard("Part 1 of 5", "Buying a pair of glasses",
    "From browsing to a placed order", 4000);

  await n.goto(BASE + "/");
  await n.say("This is the shop front. Everything starts from the menu along the top.");

  await n.click(page.locator('nav a[href="/frames"]').first());
  await n.say("“All frames” shows everything in stock.");

  await n.say("The filters down the left narrow the list — by shape, material, price and who the frames are for.");
  await n.scroll(320);

  const shape = page.locator('select, input[type="checkbox"]').first();
  if (await shape.count()) await n.moveTo(shape);
  await n.say("Pick whatever matters to you. You can combine as many filters as you like.");

  await n.scroll(-320);
  await n.click(page.locator('a[href^="/frames/"]').first());
  await n.say("Clicking a frame opens its own page.");

  await n.say("Here you see the colours it comes in, its measurements and the price.");
  await n.scroll(280);

  const rx = page.locator('input[value="prescription"]').first();
  if (await rx.count()) {
    await n.click(rx);
    await n.say("Choose “Prescription lenses” and the boxes for your prescription appear.");
  }

  await n.say("Every value is picked from a list, never typed. Real prescriptions only come in quarter steps, so a slipped finger cannot become a ruined pair of glasses.");

  const odSphere = page.locator('select[name="OdSphere"]').first();
  if (await odSphere.count()) {
    await n.select(odSphere, "-2.25");
    await n.say("This is the right eye — “OD” on your prescription.");
    const osSphere = page.locator('select[name="OsSphere"]').first();
    if (await osSphere.count()) {
      await n.select(osSphere, "-2.00");
      await n.say("And the left — “OS”. Check these two carefully: swapping them is the commonest mistake there is.");
    }
  }

  await n.scroll(260);
  await n.say("Underneath, you choose the lenses themselves — how thin, and which coatings. The price updates as you go.");

  const addToBag = page.getByRole("button", { name: /add to bag/i }).first();
  if (await addToBag.count()) {
    await n.click(addToBag, { settle: 1600 });
    await n.say("“Add to bag” puts it in your basket.");
  }

  await n.goto(BASE + "/cart");
  await n.say("The bag shows what you have chosen, with the lenses and prescription attached to each pair.");
  await n.say("If you have a discount code, it goes in here. If a code will not work, the shop tells you exactly why.");

  const checkout = page.getByRole("link", { name: /checkout/i }).first();
  if (await checkout.count()) await n.click(checkout, { settle: 1600 });

  await n.say("Checkout asks for your contact details and where to send the glasses.");

  const email = page.locator('input[name="Email"]').first();
  if (await email.count()) {
    await n.type(email, "ayesha.malik@example.com");
    await n.say("Your email address is where the confirmation and all the updates are sent, so check it carefully.");
  }
  const fullName = page.locator('input[name="FullName"]').first();
  if (await fullName.count()) await n.type(fullName, "Ayesha Malik");
  const line1 = page.locator('input[name="Line1"]').first();
  if (await line1.count()) await n.type(line1, "House 14, Gulberg III");
  const city = page.locator('input[name="City"]').first();
  if (await city.count()) await n.type(city, "Lahore");

  await n.scroll(320);
  await n.say("Then choose how you would like to pay — cash on delivery, bank transfer, or card.");
  await n.say("After you place the order, an optician checks your prescription before anything is made. That is a real person, and it is the step that protects you.");
  await n.hush();
}

async function virtualTryOn(page, n) {
  await n.titleCard("Part 2 of 5", "Trying frames on with your camera",
    "See a frame on your own face before you buy", 4000);

  await n.goto(BASE + "/try-on");
  await n.say("The virtual try-on shows what a frame looks like on your own face.");

  await n.say("Your photograph never leaves your computer. All the work happens inside your own browser — no picture of your face is ever sent to the shop.");

  const camera = page.getByRole("button", { name: /use my camera/i }).first();
  if (await camera.count()) {
    await n.moveTo(camera);
    await n.say("“Use my camera” starts it. Your browser will ask permission first — nothing happens until you allow it.");
  }

  const upload = page.getByRole("button", { name: /upload a photo/i }).first();
  if (await upload.count()) {
    await n.moveTo(upload);
    await n.say("If you would rather not use the camera, upload a straight-on photo instead.");
  }

  await n.say("In this recording there is no camera attached, so the panel stays dark. On your own computer your face appears here.");

  await n.say("Pick any frame from the panel on the right and it is placed on your face at the correct size.");
  await n.scroll(260);
  await n.beat();
  await n.scroll(-260);

  const adjust = page.getByRole("button", { name: /adjust eye points/i }).first();
  if (await adjust.count()) {
    await n.moveTo(adjust);
    await n.say("If the frame sits crookedly — bright glare or a heavy fringe can confuse it — “Adjust eye points” lets you mark the centre of each pupil yourself.");
  }

  await n.say("While it runs, the shop measures your pupillary distance: the gap between the centres of your pupils. Your lenses have to be centred on it, or strong glasses cause eye strain.");
  await n.hush();
}

async function yourAccount(page, n) {
  await n.titleCard("Part 3 of 5", "Your account",
    "Orders, addresses, appointments and your data", 4000);

  await n.goto(BASE + "/account");
  await n.say("Your account keeps your orders, your addresses and your appointments in one place.");
  await n.say("You can buy as a guest without any of this — an account simply saves you retyping.");

  await n.click(page.getByRole("link", { name: /your addresses/i }).first());
  await n.say("Saved addresses mean checkout is already filled in. The one marked “Default” is offered first.");

  await n.click(page.getByRole("link", { name: /add an address/i }).first());
  await n.say("Adding one is a short form. The label is just for you — “Home”, “Work”, “Mum's”.");
  await n.scroll(220);
  await n.beat();

  await n.goto(BASE + "/account/appointments");
  await n.say("If the practice offers sight tests, you can book online.");

  await n.click(page.getByRole("link", { name: /book an appointment/i }).first());
  await n.say("Choose a date and what the appointment is for, then click any free time.");
  await n.say("Times already taken are shown crossed out rather than hidden, so you can see how busy a day is.");
  await n.scroll(280);
  await n.beat();

  await n.goto(BASE + "/account/privacy");
  await n.say("Because this is an optical practice, it holds health information about you — so you get buttons, not a form to fill in.");
  await n.say("“Download my data” gives you a single file with everything: your details, orders, prescriptions and appointments.");
  await n.say("You can also ask the shop to correct something, or to erase you. Prescriptions and past orders are kept because the law requires it — but nothing left behind identifies you.");
  await n.hush();
}

async function shopOrders(page, n) {
  await n.titleCard("Part 4 of 5", "Running the shop",
    "The back office: orders and prescriptions", 4000);

  await n.goto(BASE + "/login");
  await n.say("Staff sign in on the same page customers use. What you can see afterwards depends on your account.");

  await n.type(page.locator('input[name="Email"]'), STAFF_EMAIL);
  await n.type(page.locator('input[name="Password"]'), STAFF_PASSWORD, { delay: 35 });
  await n.click(page.getByRole("button", { name: /^sign in$/i }).first(), { settle: 2000 });

  await n.goto(BASE + "/admin");
  await n.say("The dashboard answers one question: what needs attention today?");
  await n.say("The tiles shaded amber are work waiting on somebody. The plain ones are just information.");
  await n.scroll(300);
  await n.say("Underneath is the queue of prescriptions waiting for an optician. Nothing gets made until this list is cleared.");
  await n.scroll(-300);

  await n.click(page.locator('nav a[href="/admin/orders"]').first());
  await n.say("Every order is here. Payment and progress are shown separately — an order can be paid but not yet made, or made but not yet paid.");
  await n.say("Search by order number, email or phone, and filter by stage.");

  await n.click(page.getByRole("link", { name: /^open$/i }).first(), { settle: 1600 });
  await n.say("Opening an order shows the customer, what they bought, the prescription and where it has reached.");
  await n.scroll(340);
  await n.say("For cash on delivery or a bank transfer, “Mark as paid” records the payment — exactly the same record a card payment makes.");
  await n.scroll(280);
  await n.say("“Mark as shipped” records the courier and tracking number, and emails the customer automatically.");
  await n.scroll(-620);

  await n.click(page.locator('nav a[href="/admin/patients"]').first());
  await n.say("Every customer has a patient file, including guests. It holds their prescriptions, orders and appointments.");

  await n.click(page.getByRole("link", { name: /open|view/i }).first(), { settle: 1600 }).catch(() => {});
  await n.say("An optician checks each prescription against what the customer supplied, then clicks “Verify” — or “Send & reject” with a reason the customer can act on.");
  await n.scroll(300);
  await n.say("A prescription that has been used on an order can never be edited. A change creates a new version, and the old one stays exactly as it was.");
  await n.hush();
}

async function shopAdmin(page, n) {
  await n.titleCard("Part 5 of 5", "Keeping the shop stocked",
    "Catalogue, diary, spreadsheets and settings", 4000);

  await n.goto(BASE + "/admin/diary");
  await n.say("The diary shows who is coming in. Each booking has three buttons: “Seen”, “No show” and “Cancel”.");
  await n.say("It will not let you double-book. Two people in one chair is the mistake a diary exists to prevent.");

  await n.goto(BASE + "/admin/frames");
  await n.say("The catalogue. Each frame can have several colours, and each colour has its own stock level — so you can sell out of black without affecting tortoiseshell.");

  await n.goto(BASE + "/admin/lenses");
  await n.say("Lens options and their prices, grouped by type, thickness, coating and tint.");

  await n.goto(BASE + "/admin/media");
  await n.say("Product photographs. Drop a whole shoot in at once — each picture is turned the right way up, resized and thumbnailed automatically.");
  await n.say("Try-on artwork needs a transparent background, so tick “Keep transparency” before uploading those.");

  await n.goto(BASE + "/admin/promotions");
  await n.say("Offers and discount codes — percentage off, a fixed amount, free delivery, or buy one get one.");
  await n.say("Always set an end date. An offer with no limits is how shops lose money.");

  await n.goto(BASE + "/admin/shipping");
  await n.say("Delivery charges by area, with an estimated number of days.");

  await n.goto(BASE + "/admin/import");
  await n.say("Catalogue and patient data move in and out as spreadsheets.");
  await n.say("Always press “Check the file” first. It reports problems by the line number you see in Excel, and writes nothing until you say so.");
  await n.scroll(260);
  await n.say("Exports are shaped so you can edit them and import the same file straight back.");
  await n.scroll(-260);

  await n.goto(BASE + "/admin/data-requests");
  await n.say("When a customer asks to see, correct or erase their data, the request lands here — oldest first, because there is a legal clock running on each one.");

  await n.goto(BASE + "/admin/audit");
  await n.say("The audit trail records every change to a patient record, a price or an order: who did it, and when.");
  await n.say("It deliberately records that a prescription changed, never the values — clinical detail stays on the patient file.");

  await n.goto(BASE + "/admin/settings");
  await n.say("Finally, settings: the shop's name, currency, tax rate and which payment methods you offer.");

  await n.titleCard("That's everything", "You're ready to go",
    "The written manual covers every screen in more detail", 4500);
}

// ---------------------------------------------------------------------------

const SCENES = [
  { file: "01-buying-glasses",       chapter: "Buying glasses",      run: buyingGlasses,  auth: false },
  { file: "02-virtual-try-on",       chapter: "Virtual try-on",      run: virtualTryOn,   auth: false },
  { file: "03-your-account",         chapter: "Your account",        run: yourAccount,    auth: true  },
  { file: "04-orders-prescriptions", chapter: "Running the shop",    run: shopOrders,     auth: false },
  { file: "05-catalogue-and-admin",  chapter: "Keeping it stocked",  run: shopAdmin,      auth: true  },
];

async function signIn(page) {
  await page.goto(BASE + "/login", { waitUntil: "networkidle" });
  await page.fill('input[name="Email"]', STAFF_EMAIL);
  await page.fill('input[name="Password"]', STAFF_PASSWORD);
  await page.click('button[type="submit"]');
  await page.waitForLoadState("networkidle");
}

async function record(scene, browser) {
  const dir = path.join(TMP, scene.file);
  await rm(dir, { recursive: true, force: true });

  const context = await browser.newContext({
    viewport: SIZE,
    recordVideo: { dir, size: SIZE },
    reducedMotion: "reduce",
    colorScheme: "light",
  });

  // Pass the function and its argument, rather than stringifying a call.
  // Playwright serialises both correctly; hand-built source silently did not run.
  await context.addInitScript(FURNITURE, OVERLAY);

  const page = await context.newPage();

  if (scene.auth) await signIn(page);

  const n = narrator(page, { chapter: scene.chapter });
  await page.goto(BASE + "/", { waitUntil: "networkidle" });
  await page.waitForTimeout(800);

  await scene.run(page, n);
  await page.waitForTimeout(1200);

  // Take the handle before closing: saveAs waits for the recording to be
  // finalised. Renaming the file straight after context.close() grabs whatever
  // has been flushed so far, which silently produced an 18-second video of a
  // scene that had actually run for a minute and a half.
  const video = page.video();
  await context.close();

  await video.saveAs(path.join(OUT, `${scene.file}.webm`));
  await video.delete().catch(() => {});
  await rm(dir, { recursive: true, force: true }).catch(() => {});

  console.log(`  ok ${scene.file}.webm`);
}

async function main() {
  await mkdir(OUT, { recursive: true });
  await mkdir(TMP, { recursive: true });

  const only = process.argv[2];
  const chosen = only
    ? SCENES.filter((_, i) => String(i + 1) === only)
    : SCENES;

  const browser = await chromium.launch({ channel: "chrome", headless: true });

  console.log("\nRecording user guide");
  for (const scene of chosen) {
    try {
      await record(scene, browser);
    } catch (error) {
      console.error(`  !  ${scene.file} failed: ${error.message}`);
    }
  }

  await browser.close();
  await rm(TMP, { recursive: true, force: true });
  console.log(`\nWritten to docs/videoguide\n`);
}

main().catch((e) => { console.error("\nrecording failed:", e.message, "\n"); process.exit(1); });
