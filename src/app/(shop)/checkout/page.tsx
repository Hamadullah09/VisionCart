import Link from "next/link";
import { redirect } from "next/navigation";
import { prisma } from "@/lib/db";
import { buildCartView, peekCartToken } from "@/lib/cart";
import { getSession } from "@/lib/session";
import { quoteShipping } from "@/lib/shipping";
import { enabledPaymentMethods } from "@/lib/payments";
import { getSettingBool } from "@/lib/settings";
import { formatMoney } from "@/lib/money";
import CheckoutForm from "@/components/shop/CheckoutForm";

export const metadata = { title: "Checkout" };

export default async function CheckoutPage({ searchParams }: PageProps<"/checkout">) {
  const sp = await searchParams;
  const token = await peekCartToken();
  const cart = token ? await prisma.cart.findUnique({ where: { token } }) : null;
  if (!cart) redirect("/cart");

  const view = await buildCartView(cart.id);
  if (view.lines.length === 0) redirect("/cart");

  const session = await getSession();

  // Send guests to sign in before they fill the form rather than after.
  if (!session && !(await getSettingBool("checkout.guestAllowed"))) {
    redirect("/login?next=/checkout");
  }

  const goods = view.lines.reduce((s, l) => s + l.totalMinor, 0);

  const [quotes, defaultAddress] = await Promise.all([
    quoteShipping({
      subtotalMinor: goods - view.totals.discountMinor,
      country: "PK",
      itemCount: view.itemCount,
    }),
    session
      ? prisma.address.findFirst({
          where: { userId: session.userId },
          orderBy: [{ isDefault: "desc" }, { id: "desc" }],
        })
      : Promise.resolve(null),
  ]);

  const payments = enabledPaymentMethods();

  return (
    <div className="mx-auto max-w-6xl px-4 py-10">
      <h1 className="text-3xl font-semibold">Checkout</h1>

      {sp.cancelled && (
        <p className="mt-4 rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-800">
          Payment was cancelled — order {String(sp.cancelled)} is still waiting. You can pay again
          below or choose a different method.
        </p>
      )}

      {!session && (
        <p className="mt-4 rounded-lg bg-ink-50 px-4 py-3 text-sm">
          Checking out as a guest.{" "}
          <Link href="/login?next=/checkout" className="font-medium text-brand-600 underline">
            Sign in
          </Link>{" "}
          to reuse a saved prescription and track your order.
        </p>
      )}

      <div className="mt-8 grid gap-10 lg:grid-cols-[minmax(0,1fr)_340px]">
        <CheckoutForm
          shipping={quotes.map((q) => ({
            code: q.code,
            name: q.name,
            priceMinor: q.priceMinor,
            etaDaysMin: q.etaDaysMin,
            etaDaysMax: q.etaDaysMax,
          }))}
          payments={payments.map((p) => ({
            id: p.id,
            label: p.label,
            description: p.description,
          }))}
          defaults={{
            email: session?.email,
            fullName: session?.name ?? defaultAddress?.fullName,
            phone: defaultAddress?.phone ?? undefined,
            country: defaultAddress?.country ?? "PK",
          }}
          bankInstructions={
            process.env.BANK_TRANSFER_INSTRUCTIONS ||
            "Transfer the total to our account and email the receipt with your order number."
          }
        />

        <aside className="card h-max p-5 lg:sticky lg:top-24">
          <h2 className="font-semibold">Your order</h2>

          <ul className="mt-4 space-y-3">
            {view.lines.map((l) => (
              <li key={l.itemId} className="flex gap-3">
                <div className="grid h-14 w-16 shrink-0 place-items-center rounded bg-ink-50 p-1">
                  {l.imageUrl && (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={l.imageUrl} alt="" className="max-h-full max-w-full object-contain" />
                  )}
                </div>
                <div className="min-w-0 flex-1 text-sm">
                  <p className="truncate font-medium">{l.title}</p>
                  <p className="truncate text-xs text-ink-500">{l.lensSummary}</p>
                  <p className="text-xs text-ink-500">Qty {l.qty}</p>
                </div>
                <p className="text-sm font-medium">{formatMoney(l.totalMinor)}</p>
              </li>
            ))}
          </ul>

          <dl className="mt-5 space-y-2 border-t border-ink-200 pt-4 text-sm">
            <Row label="Frames" value={formatMoney(view.totals.subtotalMinor)} />
            {view.totals.lensTotalMinor > 0 && (
              <Row label="Lenses" value={formatMoney(view.totals.lensTotalMinor)} />
            )}
            {view.totals.discountMinor > 0 && (
              <Row
                label={view.promotions[0]?.name ?? "Discount"}
                value={`− ${formatMoney(view.totals.discountMinor)}`}
              />
            )}
            <Row
              label="Delivery"
              value={
                view.totals.shippingMinor === 0 ? "Free" : formatMoney(view.totals.shippingMinor)
              }
            />
            {view.totals.taxMinor > 0 && <Row label="Tax" value={formatMoney(view.totals.taxMinor)} />}
            <div className="flex justify-between border-t border-ink-200 pt-3 text-base font-semibold">
              <dt>Total</dt>
              <dd>{formatMoney(view.totals.totalMinor)}</dd>
            </div>
          </dl>

          <p className="mt-4 text-xs text-ink-500">
            Delivery is recalculated from your address when you place the order.
          </p>
        </aside>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between">
      <dt className="text-ink-600">{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}
