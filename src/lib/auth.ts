import "server-only";
import bcrypt from "bcryptjs";
import { redirect } from "next/navigation";
import { prisma } from "./db";
import { getSession, createSessionCookie, destroySessionCookie } from "./session";
import { STAFF_ROLES, type Role } from "./constants";

export { getSession };

const ROUNDS = 10;

export async function hashPassword(plain: string): Promise<string> {
  return bcrypt.hash(plain, ROUNDS);
}

export async function verifyPassword(plain: string, hash: string): Promise<boolean> {
  return bcrypt.compare(plain, hash);
}

export type AuthResult =
  | { ok: true }
  | { ok: false; error: string };

export async function login(email: string, password: string): Promise<AuthResult> {
  const user = await prisma.user.findUnique({
    where: { email: email.trim().toLowerCase() },
  });

  // Same message for "no such user" and "wrong password" so the form can't be
  // used to enumerate which email addresses have accounts.
  const generic = { ok: false as const, error: "Email or password is incorrect." };
  if (!user || !user.passwordHash) return generic;
  if (!user.isActive) return { ok: false, error: "This account has been disabled." };
  if (!(await verifyPassword(password, user.passwordHash))) return generic;

  await prisma.user.update({
    where: { id: user.id },
    data: { lastLoginAt: new Date() },
  });

  await createSessionCookie({
    userId: user.id,
    email: user.email,
    name: user.name,
    role: user.role as Role,
  });
  return { ok: true };
}

export async function register(input: {
  name: string;
  email: string;
  password: string;
  phone?: string;
}): Promise<AuthResult> {
  const email = input.email.trim().toLowerCase();
  const existing = await prisma.user.findUnique({ where: { email } });
  if (existing) return { ok: false, error: "An account with that email already exists." };

  const user = await prisma.user.create({
    data: {
      email,
      name: input.name.trim(),
      phone: input.phone?.trim() || null,
      passwordHash: await hashPassword(input.password),
      role: "customer",
    },
  });

  // Every customer gets a patient file from day one — the shop is an optical
  // practice, so the clinical record is the primary entity, not an add-on.
  await ensurePatientForUser(user.id);

  await createSessionCookie({
    userId: user.id,
    email: user.email,
    name: user.name,
    role: "customer",
  });
  return { ok: true };
}

export async function logout(): Promise<void> {
  await destroySessionCookie();
}

/** Next sequential patient file number, e.g. P-000042. */
export async function nextFileNo(): Promise<string> {
  const count = await prisma.patient.count();
  let n = count + 1;
  // Guard against gaps from deletions colliding with an existing number.
  for (;;) {
    const fileNo = `P-${String(n).padStart(6, "0")}`;
    const clash = await prisma.patient.findUnique({ where: { fileNo } });
    if (!clash) return fileNo;
    n += 1;
  }
}

export async function ensurePatientForUser(userId: string) {
  const existing = await prisma.patient.findUnique({ where: { userId } });
  if (existing) return existing;

  const user = await prisma.user.findUnique({ where: { id: userId } });
  if (!user) throw new Error("User not found");

  const [firstName, ...rest] = user.name.trim().split(/\s+/);
  return prisma.patient.create({
    data: {
      fileNo: await nextFileNo(),
      userId,
      firstName: firstName || user.email.split("@")[0],
      lastName: rest.join(" ") || "",
      email: user.email,
      phone: user.phone,
    },
  });
}

// --- Guards ---------------------------------------------------------------

export async function requireUser() {
  const session = await getSession();
  if (!session) redirect("/login");
  return session;
}

export async function requireStaff() {
  const session = await getSession();
  if (!session) redirect("/login?next=/admin");
  if (!STAFF_ROLES.includes(session.role)) redirect("/?error=forbidden");
  return session;
}

export async function requireAdmin() {
  const session = await getSession();
  if (!session) redirect("/login?next=/admin");
  if (session.role !== "admin") redirect("/admin?error=admin-only");
  return session;
}

/** Guard for route handlers — returns a session or null, never redirects. */
export async function apiStaff() {
  const session = await getSession();
  if (!session || !STAFF_ROLES.includes(session.role)) return null;
  return session;
}
