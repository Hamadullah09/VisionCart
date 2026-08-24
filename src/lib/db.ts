import { PrismaClient } from "@prisma/client";

// Next's dev server re-evaluates modules on every hot reload; without the
// global cache each reload would open a new pool until SQLite/Postgres refuses
// further connections.
const globalForPrisma = globalThis as unknown as { prisma?: PrismaClient };

export const prisma =
  globalForPrisma.prisma ??
  new PrismaClient({
    log: process.env.NODE_ENV === "development" ? ["warn", "error"] : ["error"],
  });

if (process.env.NODE_ENV !== "production") globalForPrisma.prisma = prisma;
