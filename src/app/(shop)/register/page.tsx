import { redirect } from "next/navigation";
import { getSession } from "@/lib/session";
import { RegisterForm } from "@/components/shop/AuthForms";

export const metadata = { title: "Create an account" };

export default async function RegisterPage({ searchParams }: PageProps<"/register">) {
  const sp = await searchParams;
  const next = typeof sp.next === "string" ? sp.next : undefined;

  if (await getSession()) redirect("/account");

  return (
    <div className="mx-auto max-w-md px-4 py-16">
      <h1 className="text-2xl font-semibold">Create an account</h1>
      <p className="mt-1 text-sm text-ink-600">
        It takes a minute and saves re-typing your prescription every time.
      </p>
      <div className="card mt-6 p-6">
        <RegisterForm next={next} />
      </div>
    </div>
  );
}
