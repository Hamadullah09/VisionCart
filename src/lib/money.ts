/**
 * All money in this app is an integer count of minor units (paisa, cents).
 * Floats are only ever produced at the last moment, for display.
 */

export const CURRENCY = process.env.NEXT_PUBLIC_CURRENCY || "PKR";
export const CURRENCY_SYMBOL = process.env.NEXT_PUBLIC_CURRENCY_SYMBOL || "Rs.";

/** Currencies whose smallest unit is the unit itself (no decimal places). */
const ZERO_DECIMAL = new Set(["JPY", "KRW", "VND", "CLP", "ISK", "XAF", "XOF"]);

export function minorPerUnit(currency = CURRENCY): number {
  return ZERO_DECIMAL.has(currency.toUpperCase()) ? 1 : 100;
}

/** 1499.5 -> 149950 */
export function toMinor(amount: number, currency = CURRENCY): number {
  return Math.round(amount * minorPerUnit(currency));
}

/** 149950 -> 1499.5 */
export function fromMinor(minor: number, currency = CURRENCY): number {
  return minor / minorPerUnit(currency);
}

/**
 * Display helper. Deliberately not Intl.NumberFormat with `style: "currency"`
 * — that renders "PKR 1,499.00" with a locale-dependent gap that shifts
 * between server and client and trips React hydration. Fixed symbol + grouped
 * digits renders identically everywhere.
 */
export function formatMoney(minor: number, currency = CURRENCY): string {
  const decimals = minorPerUnit(currency) === 1 ? 0 : 2;
  const value = fromMinor(minor, currency);
  const body = new Intl.NumberFormat("en-US", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);
  return `${CURRENCY_SYMBOL}${body}`;
}

/** Round-half-up percentage of a minor amount. bps: 1500 = 15%. */
export function applyBps(minor: number, bps: number): number {
  return Math.round((minor * bps) / 10000);
}

export function clampNonNegative(minor: number): number {
  return minor < 0 ? 0 : minor;
}
