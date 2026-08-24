import { redirect } from "next/navigation";
import { getSession } from "@/lib/session";
import { LoginForm } from "@/components/shop/AuthForms";

export const metadata = { title: "Sign in" };

export default async function LoginPage({ searchParams }: PageProps<"/login">) {
  const sp = await searchParams;
  const next = typeof sp.next === "string" ? sp.next : undefined;

  if (await getSession()) redirect(next && next.startsWith("/") ? next : "/account");

  return (
    <div className="mx-auto max-w-md px-4 py-16">
      <h1 className="text-2xl font-semibold">Sign in</h1>
      <p className="mt-1 text-sm text-ink-600">
        Your prescriptions, past pairs and try-on snapshots are all here.
      </p>
      <div className="card mt-6 p-6">
        <LoginForm next={next} />
      </div>
    </div>
  );
}
