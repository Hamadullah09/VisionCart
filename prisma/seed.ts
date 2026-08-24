/**
 * Seeds a complete, working shop: staff logins, lens pricing, a catalogue with
 * try-on artwork, delivery rates, live promotions and a demo patient with a
 * prescription and an order.
 *
 * Safe to re-run — everything is an upsert keyed on a natural identifier.
 *
 *   npm run db:seed
 */

import { PrismaClient } from "@prisma/client";
import bcrypt from "bcryptjs";
import fs from "node:fs";
import path from "node:path";

const prisma = new PrismaClient();

/** Prices are written in major units here and converted once, on the way in. */
const R = (major: number) => Math.round(major * 100);

type FrameAsset = {
  key: string;
  shape: string;
  rimType: string;
  color: string;
  colorLabel: string;
  colorHex: string;
  tinted: boolean;
  url: string;
};

function loadFrameAssets(): FrameAsset[] {
  const file = path.join(process.cwd(), "public", "frames", "manifest.json");
  if (!fs.existsSync(file)) {
    console.warn(
      "\n⚠ public/frames/manifest.json is missing — run `npm run frames:generate` first.\n" +
        "  Seeding will continue, but frames will have no images or try-on artwork.\n",
    );
    return [];
  }
  return JSON.parse(fs.readFileSync(file, "utf8")) as FrameAsset[];
}

async function seedStaff() {
  const email = (process.env.SEED_ADMIN_EMAIL || "admin@visioncart.local").toLowerCase();
  const password = process.env.SEED_ADMIN_PASSWORD || "ChangeMe123!";
  const passwordHash = await bcrypt.hash(password, 10);

  await prisma.user.upsert({
    where: { email },
    create: { email, name: "Store Owner", role: "admin", passwordHash },
    update: { role: "admin" },
  });

  await prisma.user.upsert({
    where: { email: "optician@visioncart.local" },
    create: {
      email: "optician@visioncart.local",
      name: "Dr. Ayesha Khan",
      role: "optician",
      passwordHash: await bcrypt.hash("Optician123!", 10),
    },
    update: { role: "optician" },
  });

  console.log(`  admin      ${email} / ${password}`);
  console.log(`  optician   optician@visioncart.local / Optician123!`);
}

async function seedLensOptions() {
  const options = [
    // usage
    { group: "usage", code: "use-everyday", name: "Everyday distance", priceMinor: 0, isDefault: true, position: 0,
      description: "Driving, TV, general wear." },
    { group: "usage", code: "use-reading", name: "Reading & close work", priceMinor: 0, position: 1,
      description: "Books, phone, handwork." },
    { group: "usage", code: "use-screen", name: "Screens & office", priceMinor: 0, position: 2,
      description: "Tuned for a monitor at arm's length." },

    // type
    { group: "type", code: "type-single", name: "Single vision", priceMinor: 0, isDefault: true, position: 0,
      description: "One prescription across the whole lens." },
    { group: "type", code: "type-bifocal", name: "Bifocal", priceMinor: R(3500), position: 1,
      description: "Distance on top, reading below, with a visible line." },
    { group: "type", code: "type-progressive", name: "Progressive", priceMinor: R(9500), position: 2,
      description: "Distance to reading with no line." },
    { group: "type", code: "type-office", name: "Office / desk progressive", priceMinor: R(7500), position: 3,
      description: "Optimised for screen and desk distance." },

    // index
    { group: "index", code: "idx-150", name: "1.50 standard", priceMinor: 0, isDefault: true, position: 0,
      maxSphere: 3, description: "Fine for light prescriptions." },
    { group: "index", code: "idx-161", name: "1.61 thin", priceMinor: R(2500), position: 1,
      maxSphere: 6, description: "About 20% thinner and lighter." },
    { group: "index", code: "idx-167", name: "1.67 extra thin", priceMinor: R(5500), position: 2,
      maxSphere: 9, description: "For stronger prescriptions." },
    { group: "index", code: "idx-174", name: "1.74 ultra thin", priceMinor: R(11000), position: 3,
      maxSphere: 20, description: "The thinnest we make." },

    // coating
    { group: "coating", code: "coat-hard", name: "Scratch-resistant hard coat", priceMinor: 0, isDefault: true, position: 0,
      description: "Included on every lens." },
    { group: "coating", code: "coat-ar", name: "Anti-reflective", priceMinor: R(1800), isDefault: true, position: 1,
      description: "Cuts glare from headlights and screens." },
    { group: "coating", code: "coat-blue", name: "Blue-light filter", priceMinor: R(2400), position: 2,
      description: "Takes the edge off long screen days." },
    { group: "coating", code: "coat-uv", name: "UV400 protection", priceMinor: R(900), position: 3,
      description: "Blocks UV up to 400nm." },
    { group: "coating", code: "coat-oleo", name: "Water & smudge repellent", priceMinor: R(1200), position: 4 },

    // tint
    { group: "tint", code: "tint-none", name: "Clear", priceMinor: 0, isDefault: true, position: 0 },
    { group: "tint", code: "tint-grey", name: "Solid grey", priceMinor: R(2000), position: 1,
      description: "True-to-life colour in bright sun." },
    { group: "tint", code: "tint-brown", name: "Solid brown", priceMinor: R(2000), position: 2,
      description: "Warmer, boosts contrast." },
    { group: "tint", code: "tint-photo", name: "Photochromic", priceMinor: R(6500), position: 3,
      description: "Clear indoors, dark in sunlight." },
    { group: "tint", code: "tint-polar", name: "Polarised", priceMinor: R(7500), position: 4,
      description: "Kills glare off water and roads.", excludes: "tint-photo" },

    // extra
    { group: "extra", code: "extra-case", name: "Hard case & cloth", priceMinor: 0, isDefault: true, position: 0 },
    { group: "extra", code: "extra-warranty", name: "2-year breakage cover", priceMinor: R(2500), position: 1,
      description: "One free replacement if they break." },
    { group: "extra", code: "extra-thin-edge", name: "Edge polish & bevel", priceMinor: R(1500), position: 2,
      description: "Tidier edges on stronger prescriptions." },
  ];

  for (const o of options) {
    await prisma.lensOption.upsert({
      where: { code: o.code },
      create: o,
      update: o,
    });
  }
  console.log(`  ${options.length} lens options`);
}

async function seedCategories() {
  const categories = [
    { name: "Eyeglasses", slug: "eyeglasses", position: 0 },
    { name: "Sunglasses", slug: "sunglasses", position: 1 },
    { name: "Blue-light glasses", slug: "blue-light", position: 2 },
    { name: "Reading glasses", slug: "reading", position: 3 },
    { name: "Kids", slug: "kids", position: 4 },
  ];

  for (const c of categories) {
    await prisma.category.upsert({ where: { slug: c.slug }, create: c, update: c });
  }
  return prisma.category.findMany();
}

async function seedBrands() {
  const brands = [
    { name: "Meridian", slug: "meridian", about: "Our own line — classic shapes, honest prices." },
    { name: "Aster", slug: "aster", about: "Light titanium frames for all-day wear." },
    { name: "Kestrel", slug: "kestrel", about: "Bold acetate with a bit of attitude." },
    { name: "Juno", slug: "juno", about: "Softer shapes designed for smaller faces." },
  ];

  for (const b of brands) {
    await prisma.brand.upsert({ where: { slug: b.slug }, create: b, update: b });
  }
  return prisma.brand.findMany();
}

/** Deterministic per-model details so re-seeding doesn't shuffle the shop. */
const MODELS: {
  name: string;
  shape: string;
  brand: string;
  price: number;
  compareAt?: number;
  material: string;
  gender: string;
  faceShapes: string;
  lens: number;
  bridge: number;
  temple: number;
  total: number;
  weight: number;
  categories: string[];
  featured?: boolean;
  description: string;
}[] = [
  { name: "Ravi", shape: "rectangle", brand: "meridian", price: 6500, material: "acetate", gender: "unisex",
    faceShapes: "round,oval,heart", lens: 52, bridge: 18, temple: 140, total: 138, weight: 24,
    categories: ["eyeglasses"], featured: true,
    description: "A straightforward rectangle that suits almost everyone. Deep enough for progressives." },
  { name: "Noor", shape: "round", brand: "aster", price: 8900, compareAt: 10500, material: "titanium", gender: "unisex",
    faceShapes: "square,oblong,diamond", lens: 47, bridge: 21, temple: 145, total: 133, weight: 16,
    categories: ["eyeglasses"], featured: true,
    description: "Light enough to forget you're wearing them. Softens a strong jaw." },
  { name: "Zara", shape: "cat_eye", brand: "juno", price: 7400, material: "acetate", gender: "women",
    faceShapes: "round,square,oval", lens: 53, bridge: 16, temple: 140, total: 136, weight: 22,
    categories: ["eyeglasses"], featured: true,
    description: "An upswept corner that lifts the whole face. Not shy." },
  { name: "Falcon", shape: "aviator", brand: "kestrel", price: 9800, compareAt: 12000, material: "metal", gender: "men",
    faceShapes: "square,oval,heart", lens: 58, bridge: 14, temple: 140, total: 144, weight: 28,
    categories: ["sunglasses"], featured: true,
    description: "The teardrop, done properly. Comes tinted; add polarisation for driving." },
  { name: "Harbour", shape: "wayfarer", brand: "kestrel", price: 7200, material: "acetate", gender: "unisex",
    faceShapes: "round,oval,diamond", lens: 54, bridge: 18, temple: 145, total: 142, weight: 26,
    categories: ["eyeglasses", "sunglasses"],
    description: "Thick acetate with a wide brow — the frame everyone recognises." },
  { name: "Atlas", shape: "square", brand: "meridian", price: 6900, material: "tr90", gender: "men",
    faceShapes: "round,oval", lens: 55, bridge: 17, temple: 145, total: 145, weight: 20,
    categories: ["eyeglasses", "blue-light"],
    description: "Bigger, flexible and hard to break. Good for screen days." },
  { name: "Lyra", shape: "oval", brand: "aster", price: 8200, material: "stainless", gender: "women",
    faceShapes: "square,oblong,heart", lens: 51, bridge: 19, temple: 140, total: 134, weight: 15,
    categories: ["eyeglasses", "reading"],
    description: "Semi-rimless and barely there. Reads as jewellery more than eyewear." },
  { name: "Vector", shape: "geometric", brand: "kestrel", price: 8600, material: "metal", gender: "unisex",
    faceShapes: "round,oval", lens: 50, bridge: 20, temple: 145, total: 137, weight: 19,
    categories: ["eyeglasses"],
    description: "A hexagon that stops just short of being a costume." },
  { name: "Clark", shape: "browline", brand: "meridian", price: 7800, material: "mixed", gender: "men",
    faceShapes: "oval,round,diamond", lens: 52, bridge: 19, temple: 145, total: 140, weight: 23,
    categories: ["eyeglasses"],
    description: "Heavy brow, light rim. Structure without weight." },
  { name: "Wren", shape: "rectangle", brand: "juno", price: 9600, material: "titanium", gender: "unisex",
    faceShapes: "round,heart,diamond", lens: 49, bridge: 20, temple: 140, total: 130, weight: 12,
    categories: ["eyeglasses"],
    description: "Rimless and 12 grams. Nothing between you and the world." },
];

async function seedCatalogue(
  assets: FrameAsset[],
  brands: { id: string; slug: string }[],
  categories: { id: string; slug: string }[],
) {
  const brandBySlug = new Map(brands.map((b) => [b.slug, b.id]));
  const catBySlug = new Map(categories.map((c) => [c.slug, c.id]));

  let variantCount = 0;

  for (const [i, model] of MODELS.entries()) {
    const sku = `VC-${model.name.toUpperCase().slice(0, 4)}`;
    // Rimless and semi-rimless artwork is keyed differently in the manifest.
    const wantRimless = model.name === "Wren";
    const matching = assets.filter(
      (a) =>
        a.shape === model.shape &&
        (wantRimless ? a.key.includes("rimless") : !a.key.includes("rimless")),
    );

    const frame = await prisma.frame.upsert({
      where: { sku },
      create: {
        sku,
        slug: model.name.toLowerCase(),
        name: model.name,
        brandId: brandBySlug.get(model.brand) ?? null,
        description: model.description,
        shape: model.shape,
        material: model.material,
        rimType: matching[0]?.rimType ?? "full_rim",
        gender: model.gender,
        faceShapes: model.faceShapes,
        lensWidthMm: model.lens,
        bridgeWidthMm: model.bridge,
        templeLengthMm: model.temple,
        lensHeightMm: Math.round(model.lens * 0.72),
        totalWidthMm: model.total,
        weightGrams: model.weight,
        sizeBand: model.total < 130 ? "narrow" : model.total > 143 ? "wide" : "medium",
        basePriceMinor: R(model.price),
        compareAtMinor: model.compareAt ? R(model.compareAt) : null,
        costMinor: R(Math.round(model.price * 0.42)),
        status: "active",
        isFeatured: Boolean(model.featured),
        position: i,
        metaTitle: `${model.name} — ${model.shape.replace("_", " ")} glasses`,
        metaDesc: model.description.slice(0, 155),
      },
      update: { status: "active", basePriceMinor: R(model.price), position: i },
    });

    // Categories
    for (const slug of model.categories) {
      const categoryId = catBySlug.get(slug);
      if (!categoryId) continue;
      await prisma.frameCategory.upsert({
        where: { frameId_categoryId: { frameId: frame.id, categoryId } },
        create: { frameId: frame.id, categoryId },
        update: {},
      });
    }

    // Colourways, one per matching asset
    for (const [j, asset] of matching.entries()) {
      const variantSku = `${sku}-${asset.color.toUpperCase().slice(0, 3)}`;
      const variant = await prisma.frameVariant.upsert({
        where: { sku: variantSku },
        create: {
          frameId: frame.id,
          sku: variantSku,
          colorName: asset.colorLabel,
          colorHex: asset.colorHex,
          stockQty: [12, 8, 5, 3, 0][j % 5],
          position: j,
          tryOnImageUrl: asset.url,
          // The generator draws lens centres exactly at these normalised
          // coordinates, so no per-asset tuning is needed.
          anchorLeftX: 0.29,
          anchorLeftY: 0.5,
          anchorRightX: 0.71,
          anchorRightY: 0.5,
          tryOnOpacity: asset.tinted ? 0.92 : 1,
        },
        update: { tryOnImageUrl: asset.url, isActive: true },
      });

      const existingImage = await prisma.productImage.findFirst({
        where: { variantId: variant.id },
      });
      if (!existingImage) {
        await prisma.productImage.create({
          data: {
            variantId: variant.id,
            url: asset.url,
            thumbUrl: asset.url,
            alt: `${model.name} in ${asset.colorLabel}`,
            role: "primary",
            position: 0,
          },
        });
      }
      variantCount++;
    }
  }

  console.log(`  ${MODELS.length} frames, ${variantCount} colourways`);
}

async function seedShipping() {
  const rates = [
    { name: "Standard delivery", country: "PK", priceMinor: R(300), etaDaysMin: 3, etaDaysMax: 6, carrier: "tcs", position: 0 },
    { name: "Express delivery", country: "PK", priceMinor: R(700), etaDaysMin: 1, etaDaysMax: 2, carrier: "leopards", position: 1 },
    { name: "International", country: "AE", priceMinor: R(3500), etaDaysMin: 5, etaDaysMax: 10, carrier: "dhl", position: 0 },
  ];

  for (const r of rates) {
    const existing = await prisma.shippingRate.findFirst({
      where: { name: r.name, country: r.country },
    });
    if (existing) {
      await prisma.shippingRate.update({ where: { id: existing.id }, data: r });
    } else {
      await prisma.shippingRate.create({ data: r });
    }
  }
  console.log(`  ${rates.length} delivery rates`);
}

async function seedPromotions() {
  const in30Days = new Date(Date.now() + 30 * 864e5);

  const promos = [
    {
      code: "WELCOME15",
      name: "15% off your first pair",
      description: "New customers get 15% off their first order, frame and lenses.",
      kind: "percent_off",
      value: 1500,
      maxDiscountMinor: R(3000),
      firstOrderOnly: true,
      priority: 10,
      // Stackable so it combines with the free-delivery threshold rather than
      // silently replacing it — a code that takes delivery away reads as a bug
      // to the customer even when the rules are working.
      stackable: true,
      isActive: true,
      bannerText: "New here? 15% off your first pair with code WELCOME15",
      bannerColor: "#0a67a1",
      endsAt: in30Days,
    },
    {
      code: null,
      name: "Free delivery over Rs. 15,000",
      description: "Spend a little more and delivery is on us.",
      kind: "free_shipping",
      value: 0,
      minSubtotalMinor: R(15000),
      priority: 5,
      stackable: true,
      isActive: true,
    },
    {
      code: "TWOPAIR",
      name: "Two pairs, second half price",
      description: "Buy two frames and the cheaper one is 50% off.",
      kind: "percent_off",
      value: 2500,
      minQty: 2,
      priority: 8,
      isActive: true,
    },
    {
      code: "THINLENS",
      name: "Free thin-lens upgrade",
      description: "We'll waive the cost of your thinner lenses.",
      kind: "free_lens_upgrade",
      value: 0,
      maxDiscountMinor: R(5500),
      priority: 6,
      isActive: true,
      endsAt: in30Days,
    },
  ];

  for (const p of promos) {
    if (p.code) {
      await prisma.promotion.upsert({
        where: { code: p.code },
        create: p,
        update: p,
      });
    } else {
      const existing = await prisma.promotion.findFirst({ where: { name: p.name } });
      if (existing) await prisma.promotion.update({ where: { id: existing.id }, data: p });
      else await prisma.promotion.create({ data: p });
    }
  }
  console.log(`  ${promos.length} promotions`);
}

async function seedDemoPatient() {
  const email = "demo@example.com";

  const user = await prisma.user.upsert({
    where: { email },
    create: {
      email,
      name: "Sara Ahmed",
      phone: "+92 300 1234567",
      role: "customer",
      passwordHash: await bcrypt.hash("Demo1234!", 10),
    },
    update: {},
  });

  const patient = await prisma.patient.upsert({
    where: { fileNo: "P-000001" },
    create: {
      fileNo: "P-000001",
      userId: user.id,
      firstName: "Sara",
      lastName: "Ahmed",
      email,
      phone: "+92 300 1234567",
      dateOfBirth: new Date("1991-04-18"),
      pdMm: 62.5,
      notes: "Prefers a lighter frame. Mild dry eye — recommend the anti-reflective coat.",
      consentMarketing: true,
      consentDataAt: new Date(),
      consentVersion: "v1",
    },
    update: {},
  });

  const existingRx = await prisma.prescription.findFirst({ where: { patientId: patient.id } });
  if (!existingRx) {
    await prisma.prescription.create({
      data: {
        patientId: patient.id,
        source: "in_store_exam",
        status: "verified",
        issuedAt: new Date(Date.now() - 90 * 864e5),
        expiresAt: new Date(Date.now() + 640 * 864e5),
        prescriber: "Dr. Ayesha Khan",
        clinic: "VisionCart Optical",
        verifiedBy: "Dr. Ayesha Khan",
        verifiedAt: new Date(Date.now() - 90 * 864e5),
        odSphere: -2.25,
        odCylinder: -0.75,
        odAxis: 175,
        odPdMm: 31.5,
        osSphere: -2.5,
        osCylinder: -0.5,
        osAxis: 10,
        osPdMm: 31,
      },
    });
  }

  console.log(`  demo customer ${email} / Demo1234!  (patient ${patient.fileNo})`);
}

async function seedSettings() {
  const settings: [string, string, string][] = [
    ["store.name", process.env.NEXT_PUBLIC_STORE_NAME || "VisionCart Optical", "store"],
    ["store.tagline", "Prescription eyewear, fitted properly.", "store"],
    ["store.email", "hello@visioncart.local", "store"],
    ["store.phone", "+92 300 0000000", "store"],
    ["store.address", "123 Main Boulevard, Gulberg, Lahore", "store"],
    ["store.freeShippingOverMinor", String(R(15000)), "store"],
    ["store.returnDays", "14", "store"],
    ["tryon.enabled", "true", "tryon"],
    ["tryon.cameraEnabled", "true", "tryon"],
    ["tryon.storeCustomerPhotos", "false", "tryon"],
    ["checkout.guestAllowed", "true", "checkout"],
    ["checkout.requirePrescription", "false", "checkout"],
  ];

  for (const [key, value, group] of settings) {
    await prisma.setting.upsert({
      where: { key },
      create: { key, value, group },
      update: {},
    });
  }
  console.log(`  ${settings.length} settings`);
}

async function main() {
  console.log("\nSeeding VisionCart…\n");

  const assets = loadFrameAssets();
  await seedStaff();
  await seedSettings();
  await seedLensOptions();
  const [brands, categories] = await Promise.all([seedBrands(), seedCategories()]);
  await seedCatalogue(assets, brands, categories);
  await seedShipping();
  await seedPromotions();
  await seedDemoPatient();

  console.log("\nDone. Start the shop with `npm run dev`.\n");
}

main()
  .catch((err) => {
    console.error("\nSeed failed:", err);
    process.exit(1);
  })
  .finally(() => prisma.$disconnect());
