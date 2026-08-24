import "server-only";
import { headers } from "next/headers";
import { prisma } from "./db";

/**
 * Every write to a patient record, order or price goes through here. Health
 * data needs a defensible answer to "who changed this, and when".
 */
export async function audit(args: {
  userId?: string | null;
  action: string;
  entity: string;
  entityId?: string | null;
  detail?: unknown;
}): Promise<void> {
  let ip: string | null = null;
  try {
    const h = await headers();
    ip = h.get("x-forwarded-for")?.split(",")[0].trim() || h.get("x-real-ip") || null;
  } catch {
    // Called outside a request (seed script, cron) — no client address to log.
  }

  try {
    await prisma.auditLog.create({
      data: {
        userId: args.userId ?? null,
        action: args.action,
        entity: args.entity,
        entityId: args.entityId ?? null,
        detail: args.detail === undefined ? null : JSON.stringify(args.detail),
        ip,
      },
    });
  } catch (err) {
    // An audit failure must never take down the operation it was recording.
    console.error("[audit] failed to write log entry", err);
  }
}
