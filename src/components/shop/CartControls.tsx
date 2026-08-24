"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import {
  applyPromoAction,
  removeCartItemAction,
  updateCartItemAction,
} from "@/app/actions/cart";

export function QtyControl({ itemId, qty }: { itemId: string; qty: number }) {
  const router = useRouter();
  const [pending, start] = useTransition();

  const set = (next: number) =>
    start(async () => {
      await updateCartItemAction(itemId, next);
      router.refresh();
    });

  return (
    <div className="inline-flex items-center rounded-lg border border-ink-200">
      <button
        type="button"
        onClick={() => set(qty - 1)}
        disabled={pending}
        aria-label="Decrease quantity"
        className="px-3 py-1.5 text-lg leading-none hover:bg-ink-50 disabled:opacity-40"
      >
        −
      </button>
      <span className="w-9 text-center text-sm tabular-nums">{qty}</span>
      <button
        type="button"
        onClick={() => set(qty + 1)}
        disabled={pending || qty >= 20}
        aria-label="Increase quantity"
        className="px-3 py-1.5 text-lg leading-none hover:bg-ink-50 disabled:opacity-40"
      >
        +
      </button>
    </div>
  );
}

export function RemoveButton({ itemId }: { itemId: string }) {
  const router = useRouter();
  const [pending, start] = useTransition();

  return (
    <button
      type="button"
      disabled={pending}
      onClick={() =>
        start(async () => {
          await removeCartItemAction(itemId);
          router.refresh();
        })
      }
      className="text-sm text-ink-500 underline underline-offset-2 hover:text-rose-600"
    >
      {pending ? "Removing…" : "Remove"}
    </button>
  );
}

export function PromoForm({ current, error }: { current: string | null; error?: string }) {
  const router = useRouter();
  const [code, setCode] = useState(current ?? "");
  const [pending, start] = useTransition();

  const submit = (value: string) =>
    start(async () => {
      await applyPromoAction(value);
      router.refresh();
    });

  return (
    <div>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          submit(code.trim());
        }}
        className="flex gap-2"
      >
        <input
          value={code}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
          placeholder="Promo code"
          aria-label="Promo code"
          className="field font-mono tracking-wider uppercase"
        />
        <button type="submit" disabled={pending} className="btn-secondary shrink-0">
          {pending ? "…" : "Apply"}
        </button>
      </form>

      {current && (
        <button
          type="button"
          onClick={() => {
            setCode("");
            submit("");
          }}
          className="mt-2 text-xs text-ink-500 underline underline-offset-2"
        >
          Remove code
        </button>
      )}
      {error && <p className="mt-2 text-sm text-rose-700">{error}</p>}
    </div>
  );
}
