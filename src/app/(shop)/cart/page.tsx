import Link from "next/link";
import { prisma } from "@/lib/db";
import { buildCartView, peekCartToken } from "@/lib/cart";
import { formatMoney } from "@/lib/money";
import { PromoForm, QtyControl, RemoveButton } from "@/components/shop/CartControls";

export const metadata = { title: "Your bag" };

export default async function CartPage() {
  const view = await readCart();

  if (!view || view.lines.length === 0) {
    return (
      <div className="mx-auto max-w-2xl px-4 py-20 text-center">
        <h1 className="text-2xl font-semibold">Your bag is empty</h1>
        <p className="mt-2 text-ink-600">
          Try a few frames on first — it takes about a minute and saves a return.
        </p>
        <div className="mt-6 flex justify-center gap-3">
          <Link href="/try-on" className="btn-accent">
            Start virtual try-on
          </Link>
          <Link href="/frames" className="btn-secondary">
            Browse frames
          </Link>
        </div>
      </div>
    );
  }

  const { lines, totals, promotions, promoCode, promoError } = view;

  return (
    <div className="mx-auto max-w-6xl px-4 py-10">
      <h1 className="text-3xl font-semibold">Your bag</h1>

      <div className="mt-8 grid gap-10 lg:grid-cols-[minmax(0,1fr)_340px]">
        <div className="space-y-4">
          {lines.map((line) => (
            <article key={line.itemId} className="card flex gap-4 p-4">
              <div className="grid h-24 w-32 shrink-0 place-items-center rounded-lg bg-ink-50 p-2">
                {line.imageUrl ? (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img
                    src={line.imageUrl}
                    alt={line.title}
                    className="max-h-full max-w-full object-contain"
                  />
                ) : (
                  <span className="text-xs text-ink-400">No image</span>
                )}
              </div>

              <div className="min-w-0 flex-1">
                <h2 className="font-medium">{line.title}</h2>
                <p className="mt-0.5 text-sm text-ink-600">{line.lensSummary}</p>
                <p className="mt-0.5 text-xs text-ink-400">{line.sku}</p>

                {line.warnings.map((w) => (
                  <p key={w} className="mt-2 text-sm text-amber-700">
                    {w}
                  </p>
                ))}

                <div className="mt-3 flex flex-wrap items-center gap-4">
                  <QtyControl itemId={line.itemId} qty={line.qty} />
                  <RemoveButton itemId={line.itemId} />
                </div>
              </div>

              <div className="shrink-0 text-right">
                <p className="font-semibold">{formatMoney(line.totalMinor)}</p>
                {line.lensPriceMinor > 0 && (
                  <p className="mt-1 text-xs text-ink-500">
                    {formatMoney(line.framePriceMinor)} frame
                    <br />+ {formatMoney(line.lensPriceMinor)} lenses
                  </p>
                )}
              </div>
            </article>
          ))}
        </div>

        {/* Summary */}
        <aside className="space-y-5 lg:sticky lg:top-24 lg:self-start">
          <div className="card p-5">
            <h2 className="font-semibold">Summary</h2>
            <dl className="mt-4 space-y-2 text-sm">
              <Row label="Frames" value={formatMoney(totals.subtotalMinor)} />
              {totals.lensTotalMinor > 0 && (
                <Row label="Lenses & coatings" value={formatMoney(totals.lensTotalMinor)} />
              )}
              {promotions.map((p) => (
                <Row
                  key={p.id}
                  label={p.name}
                  value={p.freeShipping && p.discountMinor === 0
                    ? "Free delivery"
                    : `− ${formatMoney(p.discountMinor)}`}
                  accent
                />
              ))}
              <Row
                label="Delivery"
                value={totals.shippingMinor === 0 ? "Free" : formatMoney(totals.shippingMinor)}
              />
              {totals.taxMinor > 0 && <Row label="Tax" value={formatMoney(totals.taxMinor)} />}
              <div className="flex justify-between border-t border-ink-200 pt-3 text-base font-semibold">
                <dt>Total</dt>
                <dd>{formatMoney(totals.totalMinor)}</dd>
              </div>
            </dl>

            <Link href="/checkout" className="btn-primary mt-5 w-full py-3 text-base">
              Checkout
            </Link>
            <Link
              href="/frames"
              className="mt-3 block text-center text-sm text-ink-600 hover:text-brand-600"
            >
              Keep shopping
            </Link>
          </div>

          <div className="card p-5">
            <h2 className="text-sm font-semibold">Have a code?</h2>
            <div className="mt-3">
              <PromoForm current={promoCode} error={promoError} />
            </div>
          </div>
        </aside>
      </div>
    </div>
  );
}

function Row({ label, value, accent }: { label: string; value: string; accent?: boolean }) {
  return (
    <div className="flex justify-between">
      <dt className={accent ? "text-emerald-700" : "text-ink-600"}>{label}</dt>
      <dd className={accent ? "text-emerald-700" : ""}>{value}</dd>
    </div>
  );
}

/**
 * Reads the visitor's cart without creating one — a plain page render is not
 * allowed to set the cart cookie, so an empty bag stays empty until they add
 * something through an action.
 */
async function readCart() {
  const token = await peekCartToken();
  if (!token) return null;
  const cart = await prisma.cart.findUnique({ where: { token }, select: { id: true } });
  if (!cart) return null;
  return buildCartView(cart.id);
}
