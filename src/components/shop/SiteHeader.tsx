import Link from "next/link";
import { getSession } from "@/lib/session";
import { STAFF_ROLES } from "@/lib/constants";
import { prisma } from "@/lib/db";
import { peekCartToken } from "@/lib/cart";
import { getSettings } from "@/lib/settings";
import { activeBanners } from "@/lib/promotions";
import { describe } from "@/lib/promotions";

export default async function SiteHeader() {
  const [session, settings, banners, cartCount] = await Promise.all([
    getSession(),
    getSettings(),
    activeBanners(),
    cartItemCount(),
  ]);

  const isStaff = session && STAFF_ROLES.includes(session.role);

  return (
    <header className="sticky top-0 z-40 border-b border-ink-200 bg-white/95 backdrop-blur">
      {banners.length > 0 && (
        <div className="overflow-hidden">
          {banners.slice(0, 1).map((b) => (
            <p
              key={b.id}
              className="px-4 py-2 text-center text-xs font-medium text-white sm:text-sm"
              style={{ background: b.bannerColor || "#0a67a1" }}
            >
              {b.bannerText || describe(b)}
              {b.code && (
                <span className="ml-2 rounded bg-white/20 px-1.5 py-0.5 font-mono tracking-wider">
                  {b.code}
                </span>
              )}
            </p>
          ))}
        </div>
      )}

      <nav className="mx-auto flex max-w-7xl items-center gap-3 px-4 py-3 sm:gap-6">
        <Link href="/" className="shrink-0 text-lg font-semibold tracking-tight">
          {settings["store.name"]}
        </Link>

        <div className="hidden items-center gap-5 text-sm md:flex">
          <Link href="/frames" className="hover:text-brand-600">
            All frames
          </Link>
          <Link href="/frames?gender=men" className="hover:text-brand-600">
            Men
          </Link>
          <Link href="/frames?gender=women" className="hover:text-brand-600">
            Women
          </Link>
          <Link href="/frames?gender=kids" className="hover:text-brand-600">
            Kids
          </Link>
          <Link href="/try-on" className="font-medium text-brand-600 hover:text-brand-700">
            Virtual try-on
          </Link>
          <Link href="/deals" className="hover:text-brand-600">
            Deals
          </Link>
        </div>

        <div className="ml-auto flex items-center gap-2 text-sm sm:gap-4">
          {isStaff && (
            <Link href="/admin" className="hidden font-medium text-ink-600 hover:text-ink-900 sm:block">
              Back office
            </Link>
          )}
          {session ? (
            <Link href="/account" className="hover:text-brand-600">
              {session.name.split(" ")[0] || "Account"}
            </Link>
          ) : (
            <Link href="/login" className="hover:text-brand-600">
              Sign in
            </Link>
          )}
          <Link
            href="/cart"
            className="relative rounded-lg border border-ink-200 px-3 py-1.5 font-medium hover:bg-ink-50"
          >
            Bag
            {cartCount > 0 && (
              <span className="absolute -top-2 -right-2 grid h-5 w-5 place-items-center rounded-full bg-brand-600 text-[11px] font-semibold text-white">
                {cartCount}
              </span>
            )}
          </Link>
        </div>
      </nav>
    </header>
  );
}

/** Header renders on every page, so this stays a single cheap aggregate. */
async function cartItemCount(): Promise<number> {
  const token = await peekCartToken();
  if (!token) return 0;
  const cart = await prisma.cart.findUnique({
    where: { token },
    select: { items: { select: { qty: true } } },
  });
  return cart?.items.reduce((s, i) => s + i.qty, 0) ?? 0;
}
