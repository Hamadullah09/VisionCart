import { getSettings } from "@/lib/settings";
import { fromMinor, CURRENCY } from "@/lib/money";
import { enabledPaymentMethods } from "@/lib/payments";
import { saveSettingsAction } from "@/app/actions/admin";

export const metadata = { title: "Settings" };

/** Checkbox keys must be listed so the action can write `false` when unticked. */
const BOOLEAN_KEYS = [
  "tryon.enabled",
  "tryon.cameraEnabled",
  "tryon.storeCustomerPhotos",
  "checkout.requirePrescription",
  "checkout.guestAllowed",
];

export default async function AdminSettingsPage() {
  const s = await getSettings();
  const payments = enabledPaymentMethods();

  return (
    <div className="max-w-3xl space-y-8">
      <header>
        <h1 className="text-2xl font-semibold">Settings</h1>
        <p className="text-sm text-ink-600">
          Everything a shop manager should be able to change. Secrets and API keys stay in the
          server&apos;s environment file — see the README.
        </p>
      </header>

      <form action={saveSettingsAction} className="space-y-6">
        <input type="hidden" name="__booleans" value={BOOLEAN_KEYS.join(",")} />

        <fieldset className="card p-5">
          <legend className="px-2 text-sm font-semibold">Store details</legend>
          <div className="grid gap-4 sm:grid-cols-2">
            <Text k="store.name" label="Store name" value={s["store.name"]} />
            <Text k="store.tagline" label="Tagline" value={s["store.tagline"]} />
            <Text k="store.email" label="Contact email" value={s["store.email"]} />
            <Text k="store.phone" label="Contact phone" value={s["store.phone"]} />
            <div className="sm:col-span-2">
              <Text k="store.address" label="Address" value={s["store.address"]} />
            </div>
          </div>
        </fieldset>

        <fieldset className="card p-5">
          <legend className="px-2 text-sm font-semibold">Selling rules</legend>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <label className="label" htmlFor="setting.store.freeShippingOverMinor">
                Free delivery over ({CURRENCY})
              </label>
              <input
                id="setting.store.freeShippingOverMinor"
                name="setting.store.freeShippingOverMinor"
                type="number"
                step="1"
                defaultValue={s["store.freeShippingOverMinor"]}
                className="field"
              />
              <p className="mt-1 text-xs text-ink-500">
                In minor units — currently{" "}
                {fromMinor(Number(s["store.freeShippingOverMinor"]) || 0).toLocaleString()}{" "}
                {CURRENCY}. Set 0 to switch it off.
              </p>
            </div>
            <Text k="store.returnDays" label="Return window (days)" value={s["store.returnDays"]} />
          </div>

          <div className="mt-4 space-y-2 text-sm">
            <Check
              k="checkout.guestAllowed"
              label="Allow checkout without an account"
              value={s["checkout.guestAllowed"]}
            />
            <Check
              k="checkout.requirePrescription"
              label="Require a prescription before checkout completes"
              value={s["checkout.requirePrescription"]}
            />
          </div>
        </fieldset>

        <fieldset className="card p-5">
          <legend className="px-2 text-sm font-semibold">Virtual try-on</legend>
          <div className="space-y-2 text-sm">
            <Check k="tryon.enabled" label="Virtual try-on is available" value={s["tryon.enabled"]} />
            <Check
              k="tryon.cameraEnabled"
              label="Offer the live camera as well as photo upload"
              value={s["tryon.cameraEnabled"]}
            />
            <Check
              k="tryon.storeCustomerPhotos"
              label="Let customers save try-on snapshots to their file"
              value={s["tryon.storeCustomerPhotos"]}
            />
          </div>
          <p className="mt-3 text-xs text-ink-500">
            Photos and camera frames are processed in the customer&apos;s browser either way, and
            only ever reach this server if the customer presses Save. Turn the last option off and
            no image is stored at all — customers can still download their own.
          </p>
        </fieldset>

        <button type="submit" className="btn-primary">
          Save settings
        </button>
      </form>

      <section className="card p-5">
        <h2 className="font-semibold">Integrations</h2>
        <p className="mt-1 text-sm text-ink-600">
          Configured in the server environment, shown here so you can confirm what is live.
        </p>

        <dl className="mt-4 space-y-2 text-sm">
          <Row
            label="Payment methods"
            value={payments.map((p) => p.label).join(", ") || "None configured"}
          />
          <Row label="Shipping" value={process.env.SHIPPING_PROVIDER || "table_rate"} />
          <Row label="Image storage" value={process.env.STORAGE_DRIVER || "local"} />
          <Row
            label="Stripe"
            value={process.env.STRIPE_SECRET_KEY ? "Keys present" : "Not configured"}
          />
          <Row
            label="Stripe webhook"
            value={process.env.STRIPE_WEBHOOK_SECRET ? "Secret present" : "Not configured"}
          />
          <Row label="Currency" value={CURRENCY} />
          <Row
            label="Tax"
            value={
              Number(process.env.TAX_RATE_BPS || 0) > 0
                ? `${Number(process.env.TAX_RATE_BPS) / 100}%${
                    process.env.TAX_INCLUSIVE === "true" ? " (included in prices)" : ""
                  }`
                : "Not charged"
            }
          />
        </dl>
      </section>
    </div>
  );
}

function Text({ k, label, value }: { k: string; label: string; value: string }) {
  return (
    <div>
      <label className="label" htmlFor={`setting.${k}`}>
        {label}
      </label>
      <input id={`setting.${k}`} name={`setting.${k}`} defaultValue={value} className="field" />
    </div>
  );
}

function Check({ k, label, value }: { k: string; label: string; value: string }) {
  return (
    <label className="flex items-center gap-2">
      <input type="checkbox" name={`setting.${k}`} defaultChecked={value === "true"} />
      {label}
    </label>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between border-b border-ink-100 py-1.5">
      <dt className="text-ink-600">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  );
}
