import { listTryOnFrames } from "@/lib/catalog";
import { getSession } from "@/lib/session";
import { getSettingBool } from "@/lib/settings";
import TryOnStudio from "@/components/tryon/TryOnStudio";

export const metadata = {
  title: "Virtual try-on",
  description:
    "Try every frame in the range on your own photo or live camera, and get your pupillary distance measured at the same time.",
};

export default async function TryOnPage({ searchParams }: PageProps<"/try-on">) {
  const sp = await searchParams;
  const [frames, session, enabled, cameraEnabled, storePhotos] = await Promise.all([
    listTryOnFrames(120),
    getSession(),
    getSettingBool("tryon.enabled"),
    getSettingBool("tryon.cameraEnabled"),
    getSettingBool("tryon.storeCustomerPhotos"),
  ]);

  const initial = typeof sp.variant === "string" ? sp.variant : undefined;

  if (!enabled) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-20 text-center">
        <h1 className="text-2xl font-semibold">Virtual try-on is switched off</h1>
        <p className="mt-2 text-ink-600">
          Our team has paused this feature. Please come into the store, or call us and we&apos;ll
          help you choose.
        </p>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-10">
      <header className="mb-8 max-w-3xl">
        <h1 className="text-3xl font-semibold">Virtual try-on</h1>
        <p className="mt-2 text-ink-600">
          Upload a straight-on photo or open your camera. We find your pupils, scale each frame to
          your face and measure your pupillary distance while you browse {frames.length} styles.
        </p>
      </header>

      {frames.length === 0 ? (
        <div className="card p-10 text-center">
          <p className="font-medium">No frames are set up for try-on yet.</p>
          <p className="mt-1 text-sm text-ink-600">
            Staff: upload a transparent PNG for a colourway in the back office to enable it here.
          </p>
        </div>
      ) : (
        <TryOnStudio
          frames={frames}
          initialVariantId={initial}
          canSave={Boolean(session) && storePhotos}
          cameraEnabled={cameraEnabled}
        />
      )}

      <section className="mt-14 grid gap-6 border-t border-ink-200 pt-10 sm:grid-cols-3">
        <Tip
          title="Get the best result"
          body="Face the camera straight on in even light, with your hair clear of your eyebrows."
        />
        <Tip
          title="About the PD measurement"
          body="We scale from the iris, which is 11.7 mm across in almost everyone. Expect ±2 mm — our optician confirms it before cutting."
        />
        <Tip
          title="Your privacy"
          body="Photos and camera frames are processed inside your browser. Nothing reaches our servers unless you press Save."
        />
      </section>
    </div>
  );
}

function Tip({ title, body }: { title: string; body: string }) {
  return (
    <div>
      <h2 className="font-semibold">{title}</h2>
      <p className="mt-1 text-sm text-ink-600">{body}</p>
    </div>
  );
}
