import { NextResponse } from "next/server";
import { markOrderPaid, markPaymentFailed } from "@/lib/payments";
import { prisma } from "@/lib/db";
import { audit } from "@/lib/audit";

/**
 * Stripe webhook — the authoritative confirmation that money moved.
 *
 * The browser returning to the success URL is a hint, not proof; a customer
 * who closes the tab mid-redirect must still get their order. Every handler
 * here is idempotent because Stripe retries.
 *
 * Local testing:
 *   stripe listen --forward-to localhost:3000/api/webhooks/stripe
 */
export async function POST(request: Request) {
  const secret = process.env.STRIPE_WEBHOOK_SECRET;
  const key = process.env.STRIPE_SECRET_KEY;
  if (!secret || !key) {
    return NextResponse.json({ error: "Stripe is not configured." }, { status: 503 });
  }

  const signature = request.headers.get("stripe-signature");
  if (!signature) return NextResponse.json({ error: "Missing signature." }, { status: 400 });

  // The raw body is required for signature verification — parsing it first
  // would change the bytes and every event would be rejected.
  const raw = await request.text();

  const { default: Stripe } = await import("stripe");
  const stripe = new Stripe(key);

  let event: import("stripe").Stripe.Event;
  try {
    event = await stripe.webhooks.constructEventAsync(raw, signature, secret);
  } catch (err) {
    console.error("[stripe] signature verification failed", err);
    return NextResponse.json({ error: "Invalid signature." }, { status: 400 });
  }

  try {
    switch (event.type) {
      case "checkout.session.completed": {
        const session = event.data.object;
        const orderId = session.metadata?.orderId ?? session.client_reference_id;
        if (orderId && session.payment_status === "paid") {
          await markOrderPaid({
            orderId,
            providerRef: session.id,
            rawPayload: { id: session.id, amount_total: session.amount_total },
          });
          await audit({
            action: "payment.succeeded",
            entity: "Order",
            entityId: orderId,
            detail: { provider: "stripe", sessionId: session.id },
          });
        }
        break;
      }

      case "checkout.session.expired": {
        const session = event.data.object;
        const orderId = session.metadata?.orderId ?? session.client_reference_id;
        if (orderId) {
          await prisma.payment.updateMany({
            where: { orderId, providerRef: session.id, status: "pending" },
            data: { status: "failed", error: "Checkout session expired" },
          });
        }
        break;
      }

      case "payment_intent.payment_failed": {
        const intent = event.data.object;
        const orderId = intent.metadata?.orderId;
        if (orderId) {
          await markPaymentFailed({
            orderId,
            error: intent.last_payment_error?.message ?? "Payment failed",
            rawPayload: { id: intent.id },
          });
        }
        break;
      }

      case "charge.refunded": {
        const charge = event.data.object;
        const orderId = charge.metadata?.orderId;
        if (orderId) {
          const fullyRefunded = charge.amount_refunded >= charge.amount;
          await prisma.order.update({
            where: { id: orderId },
            data: {
              paymentStatus: fullyRefunded ? "refunded" : "partially_refunded",
              ...(fullyRefunded ? { status: "refunded" } : {}),
            },
          });
        }
        break;
      }

      default:
        // Unhandled types are acknowledged so Stripe stops retrying them.
        break;
    }
  } catch (err) {
    console.error(`[stripe] handler for ${event.type} failed`, err);
    // 500 asks Stripe to retry — better than silently losing a paid order.
    return NextResponse.json({ error: "Handler failed." }, { status: 500 });
  }

  return NextResponse.json({ received: true });
}
