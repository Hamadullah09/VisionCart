import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({ variable: "--font-geist-mono", subsets: ["latin"] });

const storeName = process.env.NEXT_PUBLIC_STORE_NAME || "VisionCart Optical";

export const metadata: Metadata = {
  title: {
    default: `${storeName} — Prescription eyewear, fitted properly`,
    template: `%s · ${storeName}`,
  },
  description:
    "Browse prescription frames, try them on with your own photo or camera, and order lenses made to your prescription.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="flex min-h-full flex-col bg-white text-ink-900">{children}</body>
    </html>
  );
}
