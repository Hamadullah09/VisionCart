import "server-only";
import fs from "node:fs/promises";
import path from "node:path";
import crypto from "node:crypto";
import sharp from "sharp";

/**
 * Image storage behind one interface so the back office never cares where the
 * bytes live. `local` writes into /public/uploads and works with zero setup;
 * `s3` covers S3, Cloudflare R2 and DigitalOcean Spaces.
 */

export type StoredImage = {
  url: string;
  thumbUrl: string;
  filename: string;
  mimeType: string;
  sizeBytes: number;
  width: number;
  height: number;
};

const DRIVER = process.env.STORAGE_DRIVER || "local";
const LOCAL_DIR = process.env.STORAGE_LOCAL_DIR || "./public/uploads";

const MAX_BYTES = 15 * 1024 * 1024; // 15 MB per file
const ALLOWED = new Set(["image/jpeg", "image/png", "image/webp", "image/avif"]);

/** Longest edge of the stored master image; originals from phones are huge. */
const MASTER_MAX_EDGE = 2000;
const THUMB_MAX_EDGE = 400;

export class UploadError extends Error {}

function safeStem(name: string): string {
  const stem = path.parse(name).name;
  return (
    stem
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .slice(0, 60) || "image"
  );
}

/** yyyy/mm prefix keeps any single directory from growing without bound. */
function datePrefix(): string {
  const d = new Date();
  return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, "0")}`;
}

export async function storeImage(
  file: File,
  opts: { folder?: string; keepAlpha?: boolean } = {},
): Promise<StoredImage> {
  if (file.size > MAX_BYTES) {
    throw new UploadError(
      `${file.name} is ${(file.size / 1024 / 1024).toFixed(1)} MB — the limit is 15 MB.`,
    );
  }
  if (file.type && !ALLOWED.has(file.type)) {
    throw new UploadError(`${file.name}: ${file.type} is not a supported image type.`);
  }

  const input = Buffer.from(await file.arrayBuffer());
  let meta;
  try {
    meta = await sharp(input).metadata();
  } catch {
    throw new UploadError(`${file.name} could not be read as an image.`);
  }
  if (!meta.width || !meta.height) {
    throw new UploadError(`${file.name} has no readable dimensions.`);
  }

  // Try-on overlays must keep their alpha channel, so those stay PNG. Everything
  // else becomes WebP, which typically cuts catalogue weight by half or more.
  const keepAlpha = opts.keepAlpha ?? meta.hasAlpha ?? false;
  const ext = keepAlpha ? "png" : "webp";
  const mimeType = keepAlpha ? "image/png" : "image/webp";

  const pipeline = sharp(input)
    .rotate() // honour EXIF orientation, otherwise phone photos arrive sideways
    .resize({
      width: MASTER_MAX_EDGE,
      height: MASTER_MAX_EDGE,
      fit: "inside",
      withoutEnlargement: true,
    });

  const master = keepAlpha
    ? await pipeline.png({ compressionLevel: 9 }).toBuffer({ resolveWithObject: true })
    : await pipeline.webp({ quality: 82 }).toBuffer({ resolveWithObject: true });

  const thumb = await sharp(input)
    .rotate()
    .resize({
      width: THUMB_MAX_EDGE,
      height: THUMB_MAX_EDGE,
      fit: "inside",
      withoutEnlargement: true,
    })
    [keepAlpha ? "png" : "webp"]({ quality: 75 })
    .toBuffer();

  const folder = opts.folder ? `${opts.folder}/${datePrefix()}` : datePrefix();
  const id = crypto.randomBytes(6).toString("hex");
  const base = `${safeStem(file.name)}-${id}`;
  const key = `${folder}/${base}.${ext}`;
  const thumbKey = `${folder}/${base}-thumb.${ext}`;

  const [url, thumbUrl] =
    DRIVER === "s3"
      ? await Promise.all([
          putS3(key, master.data, mimeType),
          putS3(thumbKey, thumb, mimeType),
        ])
      : await Promise.all([
          putLocal(key, master.data),
          putLocal(thumbKey, thumb),
        ]);

  return {
    url,
    thumbUrl,
    filename: file.name,
    mimeType,
    sizeBytes: master.data.length,
    width: master.info.width,
    height: master.info.height,
  };
}

async function putLocal(key: string, data: Buffer): Promise<string> {
  // The path is configurable, which the bundler's static analysis reads as
  // "could be anywhere" and responds to by tracing the entire project into the
  // deployment. It is always a folder under /public, so opt out of tracing.
  const dest = path.join(/* turbopackIgnore: true */ process.cwd(), LOCAL_DIR, key);
  await fs.mkdir(path.dirname(dest), { recursive: true });
  await fs.writeFile(dest, data);
  // LOCAL_DIR lives under /public, so the public URL is the path after it.
  const publicRoot = LOCAL_DIR.replace(/^\.?\/?public/, "");
  return `${publicRoot}/${key}`.replace(/\/+/g, "/");
}

type S3ClientCtor = new (config: Record<string, unknown>) => {
  send: (command: unknown) => Promise<unknown>;
};
type PutObjectCommandCtor = new (input: Record<string, unknown>) => unknown;

async function putS3(key: string, data: Buffer, contentType: string): Promise<string> {
  const bucket = process.env.S3_BUCKET;
  if (!bucket) throw new UploadError("STORAGE_DRIVER=s3 but S3_BUCKET is not set.");

  // The AWS SDK is an optional dependency — only installed by shops that
  // actually use S3. The indirect specifier keeps the bundler from trying to
  // resolve it at build time on installs that don't have it.
  const specifier = "@aws-sdk/client-s3";
  let S3Client: S3ClientCtor;
  let PutObjectCommand: PutObjectCommandCtor;
  try {
    ({ S3Client, PutObjectCommand } = (await import(
      /* webpackIgnore: true */ specifier
    )) as unknown as { S3Client: S3ClientCtor; PutObjectCommand: PutObjectCommandCtor });
  } catch {
    throw new UploadError("S3 storage needs the AWS SDK. Run: npm install @aws-sdk/client-s3");
  }

  const client = new S3Client({
    region: process.env.S3_REGION || "auto",
    endpoint: process.env.S3_ENDPOINT || undefined,
    credentials: {
      accessKeyId: process.env.S3_ACCESS_KEY_ID!,
      secretAccessKey: process.env.S3_SECRET_ACCESS_KEY!,
    },
  });
  await client.send(
    new PutObjectCommand({
      Bucket: bucket,
      Key: key,
      Body: data,
      ContentType: contentType,
      CacheControl: "public, max-age=31536000, immutable",
    }),
  );

  const base = process.env.S3_PUBLIC_BASE_URL?.replace(/\/$/, "");
  return base ? `${base}/${key}` : `/${key}`;
}

/** Remove a stored file. Best-effort: a missing file is not an error. */
export async function deleteStored(url: string): Promise<void> {
  if (DRIVER !== "local") return; // S3 deletion is intentionally manual for now
  if (!url.startsWith("/uploads/")) return;
  const dest = path.join(process.cwd(), "public", url);
  await fs.rm(dest, { force: true });
}
