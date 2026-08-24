import Link from "next/link";
import SiteHeader from "@/components/shop/SiteHeader";
import { getSettings } from "@/lib/settings";

export default async function ShopLayout({ children }: LayoutProps<"/">) {
  const settings = await getSettings();

  return (
    <>
      <SiteHeader />
      <main className="flex-1">{children}</main>

      <footer className="mt-16 border-t border-ink-200 bg-ink-50">
        <div className="mx-auto grid max-w-7xl gap-8 px-4 py-12 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <p className="font-semibold">{settings["store.name"]}</p>
            <p className="mt-2 text-sm text-ink-600">{settings["store.tagline"]}</p>
            <p className="mt-4 text-sm text-ink-600">{settings["store.address"]}</p>
            <p className="text-sm text-ink-600">{settings["store.phone"]}</p>
            <p className="text-sm text-ink-600">{settings["store.email"]}</p>
          </div>

          <FooterCol
            title="Shop"
            links={[
              ["All frames", "/frames"],
              ["Virtual try-on", "/try-on"],
              ["Deals", "/deals"],
              ["Sunglasses", "/frames?category=sunglasses"],
            ]}
          />
          <FooterCol
            title="Your eyes"
            links={[
              ["Your account", "/account"],
              ["Your prescriptions", "/account/prescriptions"],
              ["Your orders", "/account/orders"],
              ["How to read a prescription", "/guides/prescription"],
            ]}
          />
          <FooterCol
            title="Help"
            links={[
              ["Measuring your PD", "/guides/pd"],
              ["Returns", "/guides/returns"],
              ["Privacy & your data", "/guides/privacy"],
            ]}
          />
        </div>

        <div className="border-t border-ink-200 px-4 py-6 text-center text-xs text-ink-500">
          © {new Date().getFullYear()} {settings["store.name"]}. Prescription lenses are made to the
          prescription you supply — please make sure it is current.
        </div>
      </footer>
    </>
  );
}

function FooterCol({ title, links }: { title: string; links: [string, string][] }) {
  return (
    <div>
      <p className="text-xs font-semibold tracking-wide text-ink-500 uppercase">{title}</p>
      <ul className="mt-3 space-y-2 text-sm">
        {links.map(([label, href]) => (
          <li key={href}>
            <Link href={href} className="text-ink-700 hover:text-brand-600">
              {label}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
