import Link from "next/link";
import { prisma } from "@/lib/db";
import FrameForm from "@/components/admin/FrameForm";

export const metadata = { title: "New frame" };

export default async function NewFramePage() {
  const brands = await prisma.brand.findMany({
    where: { isActive: true },
    select: { id: true, name: true },
    orderBy: { name: "asc" },
  });

  return (
    <div className="max-w-5xl space-y-6">
      <div>
        <Link href="/admin/frames" className="text-sm text-brand-600">
          ← Frames
        </Link>
        <h1 className="mt-1 text-2xl font-semibold">New frame</h1>
        <p className="text-sm text-ink-600">
          Save this first — colourways, stock, photos and the try-on artwork are added next.
        </p>
      </div>

      <FrameForm brands={brands} />
    </div>
  );
}
