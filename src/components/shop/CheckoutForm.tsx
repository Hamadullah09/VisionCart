"use client";

import { useActionState, useState } from "react";
import { placeOrderAction, type CheckoutState } from "@/app/actions/checkout";
import { formatMoney } from "@/lib/money";

export type ShippingChoice = {
  code: string;
  name: string;
  priceMinor: number;
  etaDaysMin: number;
  etaDaysMax: number;
};

export type PaymentChoice = {
  id: string;
  label: string;
  description: string;
};

export default function CheckoutForm({
  shipping,
  payments,
  defaults,
  bankInstructions,
}: {
  shipping: ShippingChoice[];
  payments: PaymentChoice[];
  defaults: { email?: string; fullName?: string; phone?: string; country: string };
  bankInstructions: string;
}) {
  const [state, action, pending] = useActionState<CheckoutState, FormData>(placeOrderAction, {});
  const [method, setMethod] = useState(payments[0]?.id ?? "");

  const err = (field: string) => state.fieldErrors?.[field];

  return (
    <form action={action} className="space-y-8">
      <section className="card p-5">
        <h2 className="font-semibold">Contact</h2>
        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <Field
            name="email"
            label="Email"
            type="email"
            required
            defaultValue={defaults.email}
            error={err("email")}
            hint="Order updates and your prescription link go here."
          />
          <Field
            name="phone"
            label="Phone"
            required
            defaultValue={defaults.phone}
            error={err("phone")}
            hint="The courier calls before delivery."
          />
        </div>
      </section>

      <section className="card p-5">
        <h2 className="font-semibold">Delivery address</h2>
        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <Field
              name="fullName"
              label="Full name"
              required
              defaultValue={defaults.fullName}
              error={err("fullName")}
            />
          </div>
          <div className="sm:col-span-2">
            <Field name="line1" label="Address" required error={err("line1")} />
          </div>
          <div className="sm:col-span-2">
            <Field name="line2" label="Apartment, suite (optional)" error={err("line2")} />
          </div>
          <Field name="city" label="City" required error={err("city")} />
          <Field name="state" label="Province / state" error={err("state")} />
          <Field name="postalCode" label="Postal code" error={err("postalCode")} />
          <div>
            <label className="label" htmlFor="country">
              Country
            </label>
            <select
              id="country"
              name="country"
              defaultValue={defaults.country}
              className="field"
            >
              <option value="PK">Pakistan</option>
              <option value="AE">United Arab Emirates</option>
              <option value="GB">United Kingdom</option>
              <option value="US">United States</option>
              <option value="CA">Canada</option>
              <option value="AU">Australia</option>
            </select>
          </div>
        </div>

        <label className="mt-4 flex items-center gap-2 text-sm">
          <input type="checkbox" name="saveAddress" defaultChecked />
          Save this address for next time
        </label>
      </section>

      <section className="card p-5">
        <h2 className="font-semibold">Delivery method</h2>
        <div className="mt-4 space-y-2">
          {shipping.map((s, i) => (
            <label
              key={s.code}
              className="flex cursor-pointer items-center gap-3 rounded-lg border border-ink-200 p-3 hover:bg-ink-50"
            >
              <input type="radio" name="shippingCode" value={s.code} defaultChecked={i === 0} />
              <span className="flex-1">
                <span className="block text-sm font-medium">{s.name}</span>
                <span className="block text-xs text-ink-500">
                  Arrives in {s.etaDaysMin}–{s.etaDaysMax} working days
                </span>
              </span>
              <span className="text-sm font-medium">
                {s.priceMinor === 0 ? "Free" : formatMoney(s.priceMinor)}
              </span>
            </label>
          ))}
        </div>
      </section>

      <section className="card p-5">
        <h2 className="font-semibold">Payment</h2>
        {err("paymentMethod") && (
          <p className="mt-2 text-sm text-rose-700">{err("paymentMethod")}</p>
        )}
        <div className="mt-4 space-y-2">
          {payments.map((p) => (
            <label
              key={p.id}
              className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 ${
                method === p.id ? "border-ink-900 bg-ink-50" : "border-ink-200 hover:bg-ink-50"
              }`}
            >
              <input
                type="radio"
                name="paymentMethod"
                value={p.id}
                checked={method === p.id}
                onChange={() => setMethod(p.id)}
                className="mt-1"
              />
              <span>
                <span className="block text-sm font-medium">{p.label}</span>
                <span className="block text-xs text-ink-500">{p.description}</span>
              </span>
            </label>
          ))}
        </div>

        {method === "bank_transfer" && (
          <p className="mt-4 rounded-lg bg-ink-50 p-3 text-sm whitespace-pre-line text-ink-700">
            {bankInstructions}
          </p>
        )}
        {method === "stripe" && (
          <p className="mt-4 text-sm text-ink-600">
            You&apos;ll be taken to Stripe&apos;s secure page to pay, then brought straight back.
          </p>
        )}
      </section>

      <section className="card p-5">
        <label className="label" htmlFor="notes">
          Anything we should know? (optional)
        </label>
        <textarea
          id="notes"
          name="notes"
          rows={3}
          className="field"
          placeholder="Delivery instructions, or details about your prescription."
        />
      </section>

      {state.error && (
        <p className="rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-700" role="alert">
          {state.error}
        </p>
      )}

      <button type="submit" disabled={pending} className="btn-primary w-full py-3.5 text-base">
        {pending ? "Placing your order…" : "Place order"}
      </button>

      <p className="text-center text-xs text-ink-500">
        By ordering you confirm the prescription details you gave us are current and correct.
      </p>
    </form>
  );
}

function Field({
  name,
  label,
  type = "text",
  required,
  defaultValue,
  error,
  hint,
}: {
  name: string;
  label: string;
  type?: string;
  required?: boolean;
  defaultValue?: string;
  error?: string;
  hint?: string;
}) {
  return (
    <div>
      <label className="label" htmlFor={name}>
        {label}
        {required && <span className="ml-0.5 text-rose-600">*</span>}
      </label>
      <input
        id={name}
        name={name}
        type={type}
        required={required}
        defaultValue={defaultValue}
        aria-invalid={Boolean(error)}
        className={`field ${error ? "border-rose-400" : ""}`}
      />
      {error ? (
        <p className="mt-1 text-xs text-rose-700">{error}</p>
      ) : hint ? (
        <p className="mt-1 text-xs text-ink-500">{hint}</p>
      ) : null}
    </div>
  );
}
