import "server-only";
import { prisma } from "./db";

/**
 * Store configuration the owner can change without a deploy. Env vars stay for
 * secrets and infrastructure; anything a shop manager should own lives here.
 */

export const SETTING_DEFAULTS = {
  "store.name": process.env.NEXT_PUBLIC_STORE_NAME || "VisionCart Optical",
  "store.tagline": "Prescription eyewear, fitted properly.",
  "store.email": "hello@example.com",
  "store.phone": "+92 300 0000000",
  "store.address": "123 Main Boulevard, Lahore",
  "store.freeShippingOverMinor": "1500000",
  "store.returnDays": "14",
  "tryon.enabled": "true",
  "tryon.cameraEnabled": "true",
  "tryon.storeCustomerPhotos": "false",
  "checkout.requirePrescription": "false",
  "checkout.guestAllowed": "true",
} as const;

export type SettingKey = keyof typeof SETTING_DEFAULTS;

export async function getSettings(): Promise<Record<string, string>> {
  const rows = await prisma.setting.findMany();
  const map: Record<string, string> = { ...SETTING_DEFAULTS };
  for (const r of rows) map[r.key] = r.value;
  return map;
}

export async function getSetting(key: SettingKey | string): Promise<string> {
  const row = await prisma.setting.findUnique({ where: { key } });
  return row?.value ?? (SETTING_DEFAULTS as Record<string, string>)[key] ?? "";
}

export async function getSettingBool(key: SettingKey | string): Promise<boolean> {
  return (await getSetting(key)) === "true";
}

export async function getSettingInt(key: SettingKey | string, fallback = 0): Promise<number> {
  const n = Number(await getSetting(key));
  return Number.isFinite(n) ? n : fallback;
}

export async function setSetting(key: string, value: string, group = "general") {
  return prisma.setting.upsert({
    where: { key },
    create: { key, value, group },
    update: { value, group },
  });
}
