import "server-only";
import { prisma } from "./db";
import { getSettingInt } from "./settings";

/**
 * Shipping behind one interface.
 *
 *   table_rate  — rows in the ShippingRate table, editable in the back office.
 *                 Works offline, no account needed. This is the default.
 *   shippo      — live carrier rates + printable labels via goshippo.com
 *   easypost    — live carrier rates + printable labels via easypost.com
 *
 * Both live providers fall back to the rate table if their key is missing or
 * the API call fails, so a carrier outage degrades to flat-rate rather than
 * blocking checkout.
 */

export type ShippingQuote = {
  code: string;
  name: string;
  carrier: string;
  priceMinor: number;
  etaDaysMin: number;
  etaDaysMax: number;
  provider: string;
  /** Provider rate id, needed later to buy the label. */
  rateRef?: string;
};

export type ShipAddress = {
  fullName: string;
  line1: string;
  line2?: string | null;
  city: string;
  state?: string | null;
  postalCode?: string | null;
  country: string;
  phone?: string | null;
  email?: string | null;
};

const PROVIDER = process.env.SHIPPING_PROVIDER || "table_rate";

/** A boxed pair of glasses with a case, near enough for rating purposes. */
const DEFAULT_PARCEL = { lengthCm: 18, widthCm: 9, heightCm: 6, weightGrams: 350 };

export function shipFromAddress(): ShipAddress {
  return {
    fullName: process.env.SHIP_FROM_NAME || "Optical Store",
    line1: process.env.SHIP_FROM_LINE1 || "",
    city: process.env.SHIP_FROM_CITY || "",
    state: process.env.SHIP_FROM_STATE || "",
    postalCode: process.env.SHIP_FROM_POSTAL || "",
    country: process.env.SHIP_FROM_COUNTRY || "PK",
    phone: process.env.SHIP_FROM_PHONE || "",
  };
}

export async function quoteShipping(args: {
  subtotalMinor: number;
  country: string;
  state?: string | null;
  postalCode?: string | null;
  address?: ShipAddress | null;
  itemCount?: number;
}): Promise<ShippingQuote[]> {
  const table = await tableRates(args);

  if (PROVIDER === "shippo" && process.env.SHIPPO_API_KEY && args.address) {
    const live = await shippoRates(args.address, args.itemCount ?? 1).catch((e) => {
      console.error("[shipping] Shippo rating failed, using rate table", e);
      return [];
    });
    if (live.length) return live;
  }

  if (PROVIDER === "easypost" && process.env.EASYPOST_API_KEY && args.address) {
    const live = await easypostRates(args.address, args.itemCount ?? 1).catch((e) => {
      console.error("[shipping] EasyPost rating failed, using rate table", e);
      return [];
    });
    if (live.length) return live;
  }

  return table;
}

// --- Rate table -----------------------------------------------------------

async function tableRates(args: {
  subtotalMinor: number;
  country: string;
  state?: string | null;
}): Promise<ShippingQuote[]> {
  const freeOver = await getSettingInt("store.freeShippingOverMinor", 0);

  const rows = await prisma.shippingRate.findMany({
    where: { isActive: true, country: args.country.toUpperCase() },
    orderBy: [{ position: "asc" }, { priceMinor: "asc" }],
  });

  const matching = rows.filter(
    (r) =>
      args.subtotalMinor >= r.minSubtotalMinor &&
      (r.maxSubtotalMinor == null || args.subtotalMinor <= r.maxSubtotalMinor) &&
      (!r.region || !args.state || r.region.toLowerCase() === args.state.toLowerCase()),
  );

  const source = matching.length ? matching : rows;
  if (source.length === 0) {
    // Nothing configured for this country yet — quote a single free line so a
    // fresh install can still take an order.
    return [
      {
        code: "standard",
        name: "Standard delivery",
        carrier: "local",
        priceMinor: 0,
        etaDaysMin: 3,
        etaDaysMax: 7,
        provider: "table_rate",
      },
    ];
  }

  return source.map((r) => ({
    code: r.id,
    name: r.name,
    carrier: r.carrier || "local",
    priceMinor: freeOver > 0 && args.subtotalMinor >= freeOver ? 0 : r.priceMinor,
    etaDaysMin: r.etaDaysMin,
    etaDaysMax: r.etaDaysMax,
    provider: "table_rate",
  }));
}

// --- Shippo ---------------------------------------------------------------

type ShippoRate = {
  object_id: string;
  amount: string;
  currency: string;
  provider: string;
  servicelevel?: { name?: string };
  estimated_days?: number;
};

async function shippoRates(to: ShipAddress, itemCount: number): Promise<ShippingQuote[]> {
  const res = await fetch("https://api.goshippo.com/shipments/", {
    method: "POST",
    headers: {
      Authorization: `ShippoToken ${process.env.SHIPPO_API_KEY}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      address_from: toShippoAddress(shipFromAddress()),
      address_to: toShippoAddress(to),
      parcels: [
        {
          length: String(DEFAULT_PARCEL.lengthCm),
          width: String(DEFAULT_PARCEL.widthCm),
          height: String(DEFAULT_PARCEL.heightCm * Math.max(1, itemCount)),
          distance_unit: "cm",
          weight: String(DEFAULT_PARCEL.weightGrams * Math.max(1, itemCount)),
          mass_unit: "g",
        },
      ],
      async: false,
    }),
    signal: AbortSignal.timeout(12_000),
  });

  if (!res.ok) throw new Error(`Shippo ${res.status}: ${await res.text()}`);
  const data = (await res.json()) as { rates?: ShippoRate[] };

  return (data.rates ?? []).map((r) => ({
    code: r.object_id,
    name: r.servicelevel?.name || r.provider,
    carrier: r.provider.toLowerCase(),
    priceMinor: Math.round(parseFloat(r.amount) * 100),
    etaDaysMin: Math.max(1, (r.estimated_days ?? 5) - 1),
    etaDaysMax: (r.estimated_days ?? 5) + 1,
    provider: "shippo",
    rateRef: r.object_id,
  }));
}

function toShippoAddress(a: ShipAddress) {
  return {
    name: a.fullName,
    street1: a.line1,
    street2: a.line2 || "",
    city: a.city,
    state: a.state || "",
    zip: a.postalCode || "",
    country: a.country,
    phone: a.phone || "",
    email: a.email || "",
  };
}

/** Buy a Shippo label for a chosen rate. Returns tracking + label PDF. */
export async function shippoBuyLabel(rateRef: string) {
  const res = await fetch("https://api.goshippo.com/transactions/", {
    method: "POST",
    headers: {
      Authorization: `ShippoToken ${process.env.SHIPPO_API_KEY}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ rate: rateRef, label_file_type: "PDF", async: false }),
    signal: AbortSignal.timeout(20_000),
  });
  if (!res.ok) throw new Error(`Shippo label ${res.status}: ${await res.text()}`);
  const t = (await res.json()) as {
    object_id: string;
    tracking_number?: string;
    tracking_url_provider?: string;
    label_url?: string;
    status: string;
  };
  return {
    providerRef: t.object_id,
    trackingNumber: t.tracking_number ?? null,
    trackingUrl: t.tracking_url_provider ?? null,
    labelUrl: t.label_url ?? null,
  };
}

// --- EasyPost -------------------------------------------------------------

type EasyPostRate = {
  id: string;
  rate: string;
  carrier: string;
  service: string;
  delivery_days?: number | null;
};

async function easypostRates(to: ShipAddress, itemCount: number): Promise<ShippingQuote[]> {
  const auth = "Basic " + Buffer.from(`${process.env.EASYPOST_API_KEY}:`).toString("base64");
  const from = shipFromAddress();

  const res = await fetch("https://api.easypost.com/v2/shipments", {
    method: "POST",
    headers: { Authorization: auth, "Content-Type": "application/json" },
    body: JSON.stringify({
      shipment: {
        to_address: toEasyPostAddress(to),
        from_address: toEasyPostAddress(from),
        parcel: {
          length: DEFAULT_PARCEL.lengthCm / 2.54,
          width: DEFAULT_PARCEL.widthCm / 2.54,
          height: (DEFAULT_PARCEL.heightCm * Math.max(1, itemCount)) / 2.54,
          weight: (DEFAULT_PARCEL.weightGrams * Math.max(1, itemCount)) / 28.35,
        },
      },
    }),
    signal: AbortSignal.timeout(12_000),
  });

  if (!res.ok) throw new Error(`EasyPost ${res.status}: ${await res.text()}`);
  const data = (await res.json()) as { rates?: EasyPostRate[] };

  return (data.rates ?? []).map((r) => ({
    code: r.id,
    name: `${r.carrier} ${r.service}`,
    carrier: r.carrier.toLowerCase(),
    priceMinor: Math.round(parseFloat(r.rate) * 100),
    etaDaysMin: Math.max(1, (r.delivery_days ?? 5) - 1),
    etaDaysMax: (r.delivery_days ?? 5) + 1,
    provider: "easypost",
    rateRef: r.id,
  }));
}

function toEasyPostAddress(a: ShipAddress) {
  return {
    name: a.fullName,
    street1: a.line1,
    street2: a.line2 || undefined,
    city: a.city,
    state: a.state || undefined,
    zip: a.postalCode || undefined,
    country: a.country,
    phone: a.phone || undefined,
    email: a.email || undefined,
  };
}

/**
 * Create the shipment record for an order. With a live provider configured and
 * a rate reference this buys a real label; otherwise it opens a manual
 * shipment the team completes with a courier's tracking number by hand.
 */
export async function createShipmentForOrder(args: {
  orderId: string;
  carrier: string;
  service?: string;
  costMinor: number;
  rateRef?: string | null;
}) {
  let label: Awaited<ReturnType<typeof shippoBuyLabel>> | null = null;

  if (PROVIDER === "shippo" && process.env.SHIPPO_API_KEY && args.rateRef) {
    label = await shippoBuyLabel(args.rateRef).catch((e) => {
      console.error("[shipping] label purchase failed — created manual shipment", e);
      return null;
    });
  }

  return prisma.shipment.create({
    data: {
      orderId: args.orderId,
      carrier: args.carrier,
      service: args.service ?? null,
      costMinor: args.costMinor,
      trackingNumber: label?.trackingNumber ?? null,
      trackingUrl: label?.trackingUrl ?? null,
      labelUrl: label?.labelUrl ?? null,
      providerRef: label?.providerRef ?? null,
      status: label ? "label_created" : "pending",
    },
  });
}
