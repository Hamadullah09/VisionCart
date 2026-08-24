"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { login, register, logout } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { prisma } from "@/lib/db";
import { getOrCreateCart } from "@/lib/cart";

export type AuthState = { error?: string };

const loginSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  password: z.string().min(1, "Enter your password."),
  next: z.string().optional(),
});

export async function loginAction(_prev: AuthState, formData: FormData): Promise<AuthState> {
  const parsed = loginSchema.safeParse(Object.fromEntries(formData));
  if (!parsed.success) return { error: parsed.error.issues[0].message };

  const result = await login(parsed.data.email, parsed.data.password);
  if (!result.ok) return { error: result.error };

  // Carry a guest bag over to the account that just signed in.
  await getOrCreateCart();

  const user = await prisma.user.findUnique({
    where: { email: parsed.data.email.toLowerCase() },
    select: { id: true },
  });
  await audit({ userId: user?.id, action: "auth.login", entity: "User", entityId: user?.id });

  redirect(safeNext(parsed.data.next));
}

const registerSchema = z
  .object({
    name: z.string().min(2, "Tell us your name.").max(120),
    email: z.string().email("Enter a valid email address."),
    phone: z.string().max(30).optional(),
    password: z.string().min(8, "Use at least 8 characters."),
    confirm: z.string(),
    next: z.string().optional(),
  })
  .refine((v) => v.password === v.confirm, {
    path: ["confirm"],
    message: "The two passwords don't match.",
  });

export async function registerAction(_prev: AuthState, formData: FormData): Promise<AuthState> {
  const parsed = registerSchema.safeParse(Object.fromEntries(formData));
  if (!parsed.success) return { error: parsed.error.issues[0].message };

  const result = await register(parsed.data);
  if (!result.ok) return { error: result.error };

  await getOrCreateCart();
  redirect(safeNext(parsed.data.next));
}

export async function logoutAction() {
  await logout();
  redirect("/");
}

/** Only ever redirect within this site — an open redirect is a phishing gift. */
function safeNext(next?: string): string {
  if (!next) return "/account";
  if (!next.startsWith("/") || next.startsWith("//")) return "/account";
  return next;
}
