import Link from "next/link";
import { requireStaff } from "@/lib/auth";
import { ROLE_LABELS } from "@/lib/constants";
import { logoutAction } from "@/app/actions/auth";

const NAV: { href: string; label: string; hint: string }[] = [
  { href: "/admin", label: "Dashboard", hint: "Today at a glance" },
  { href: "/admin/orders", label: "Orders", hint: "Take payment, move to lab, ship" },
  { href: "/admin/patients", label: "Patients", hint: "Files, prescriptions, history" },
  { href: "/admin/frames", label: "Frames", hint: "Catalogue and stock" },
  { href: "/admin/media", label: "Media", hint: "Bulk image upload" },
  { href: "/admin/lenses", label: "Lenses", hint: "Options and pricing" },
  { href: "/admin/promotions", label: "Promotions", hint: "Deals and codes" },
  { href: "/admin/import", label: "Import", hint: "CSV in and out" },
  { href: "/admin/settings", label: "Settings", hint: "Store details" },
];

export default async function AdminLayout({ children }: LayoutProps<"/admin">) {
  const session = await requireStaff();

  return (
    <div className="flex min-h-screen flex-col lg:flex-row">
      <aside className="no-print shrink-0 border-b border-ink-200 bg-ink-900 text-ink-100 lg:w-60 lg:border-r lg:border-b-0">
        <div className="p-4">
          <Link href="/admin" className="block text-lg font-semibold text-white">
            Back office
          </Link>
          <p className="mt-0.5 text-xs text-ink-400">
            {session.name} · {ROLE_LABELS[session.role]}
          </p>
        </div>

        <nav className="flex gap-1 overflow-x-auto p-2 lg:flex-col lg:overflow-visible">
          {NAV.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              title={item.hint}
              className="rounded-lg px-3 py-2 text-sm whitespace-nowrap text-ink-200 transition hover:bg-ink-800 hover:text-white"
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="mt-auto hidden p-4 lg:block">
          <Link href="/" className="block text-xs text-ink-400 hover:text-white">
            ← View the shop
          </Link>
          <form action={logoutAction} className="mt-2">
            <button type="submit" className="text-xs text-ink-400 hover:text-white">
              Sign out
            </button>
          </form>
        </div>
      </aside>

      <main className="min-w-0 flex-1 bg-ink-50/50 p-4 lg:p-8">{children}</main>
    </div>
  );
}
