import Link from "next/link";
import { prisma } from "@/lib/db";
import PromotionForm from "@/components/admin/PromotionForm";

export const metadata = { title: "New deal" };

export default async function NewPromotionPage() {
  const [brands, categories] = await Promise.all([
    prisma.brand.findMany({ select: { id: true, name: true }, orderBy: { name: "asc" } }),
    prisma.category.findMany({ select: { id: true, name: true }, orderBy: { position: "asc" } }),
  ]);

  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <Link href="/admin/promotions" className="text-sm text-brand-600">
          ← Promotions
        </Link>
        <h1 className="mt-1 text-2xl font-semibold">New deal</h1>
      </div>
      <PromotionForm brands={brands} categories={categories} />
    </div>
  );
}
