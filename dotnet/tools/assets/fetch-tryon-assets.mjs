#!/usr/bin/env node
/**
 * Puts the virtual try-on's face-detection assets into /public:
 *
 *   wwwroot/wasm/*                    copied from node_modules (offline)
 *   wwwroot/models/face_landmarker.task   downloaded once from Google's CDN
 *
 * Self-hosting both means the storefront makes no third-party requests at
 * runtime — customers' faces never leave the machine they are sitting at, and
 * the shop keeps working if the CDN is blocked or down.
 *
 * If the download fails the app still runs: the try-on detects the missing
 * model and offers manual pupil placement instead.
 */

import fs from "node:fs/promises";
import path from "node:path";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const root = process.cwd();

const MODEL_URL =
  process.env.TRYON_MODEL_SOURCE ||
  "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task";

import { fileURLToPath } from "node:url";

/** This file now lives in the .NET project; wwwroot is the static root. */
const HERE = path.dirname(fileURLToPath(import.meta.url));
const WWWROOT = path.join(HERE, "..", "..", "src", "VisionCart.Web", "wwwroot");

const wasmDest = path.join(WWWROOT, "wasm");
const modelDest = path.join(WWWROOT, "models", "face_landmarker.task");

async function resolveWasmDir() {
  // The package's "exports" map hides package.json, so resolve the entry point
  // and walk up from there; fall back to the conventional install path.
  const candidates = [];
  try {
    candidates.push(path.join(path.dirname(require.resolve("@mediapipe/tasks-vision")), "wasm"));
  } catch {
    // Not resolvable from here — the fallback below still covers a normal install.
  }
  candidates.push(path.join(root, "node_modules", "@mediapipe", "tasks-vision", "wasm"));

  for (const dir of candidates) {
    try {
      await fs.access(dir);
      return dir;
    } catch {
      // Try the next candidate.
    }
  }
  return null;
}

async function copyWasm() {
  const src = await resolveWasmDir();
  if (!src) {
    console.error("✗ @mediapipe/tasks-vision is not installed. Run: npm install");
    return false;
  }

  const files = await fs.readdir(src);
  await fs.mkdir(wasmDest, { recursive: true });
  for (const f of files) {
    await fs.copyFile(path.join(src, f), path.join(wasmDest, f));
  }
  console.log(`✓ copied ${files.length} wasm files to public/wasm`);
  return true;
}

async function fetchModel() {
  try {
    const stat = await fs.stat(modelDest);
    if (stat.size > 1_000_000) {
      console.log("✓ face_landmarker.task already present, skipping download");
      return true;
    }
  } catch {
    // Not there yet — fall through and download.
  }

  console.log(`… downloading face landmark model (~3.7 MB)`);
  try {
    const res = await fetch(MODEL_URL, { signal: AbortSignal.timeout(120_000) });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const buf = Buffer.from(await res.arrayBuffer());
    if (buf.length < 1_000_000) throw new Error(`response was only ${buf.length} bytes`);

    await fs.mkdir(path.dirname(modelDest), { recursive: true });
    await fs.writeFile(modelDest, buf);
    console.log(`✓ saved wwwroot/models/face_landmarker.task (${(buf.length / 1e6).toFixed(1)} MB)`);
    return true;
  } catch (err) {
    console.warn(`\n⚠ Could not download the face landmark model: ${err.message}`);
    console.warn("  The shop will still run — virtual try-on will ask customers to");
    console.warn("  place two pupil markers by hand instead of detecting them.");
    console.warn("  To fix later, re-run:  npm run tryon:assets");
    console.warn(`  Or download manually to wwwroot/models/face_landmarker.task from:\n  ${MODEL_URL}\n`);
    return false;
  }
}

const wasmOk = await copyWasm();
const modelOk = await fetchModel();

if (wasmOk && modelOk) {
  console.log("\nVirtual try-on is ready with automatic face detection.");
} else {
  console.log("\nVirtual try-on will run in manual placement mode.");
}
