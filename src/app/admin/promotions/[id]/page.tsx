import Link from "next/link";
import { notFound } from "next/navigation";
import { prisma } from "@/lib/db";
import PromotionForm from "@/components/admin/PromotionForm";

export const metadata = { title: "Edit deal" };

export default async function EditPromotionPage({ params }: PageProps<"/admin/promotions/[id]">) {
  const { id } = await params;

  const [promotion, brands, categories] = await Promise.all([
    prisma.promotion.findUnique({ where: { id } }),
    prisma.brand.findMany({ select: { id: true, name: true }, orderBy: { name: "asc" } }),
    prisma.category.findMany({ select: { id: true, name: true }, orderBy: { position: "asc" } }),
  ]);

  if (!promotion) notFound();

  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <Link href="/admin/promotions" className="text-sm text-brand-600">
          ← Promotions
        </Link>
        <h1 className="mt-1 text-2xl font-semibold">{promotion.name}</h1>
        <p className="text-sm text-ink-500">
          Used {promotion.usageCount} time{promotion.usageCount === 1 ? "" : "s"}
        </p>
      </div>
      <PromotionForm promotion={promotion} brands={brands} categories={categories} />
    </div>
  );
}
