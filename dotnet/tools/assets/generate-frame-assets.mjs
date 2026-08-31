#!/usr/bin/env node
/**
 * Draws the frame artwork the catalogue and the virtual try-on both use.
 *
 * Real product photography replaces these later — upload a transparent PNG in
 * the back office and calibrate it there. Until then this gives a complete,
 * working shop on a fresh install.
 *
 * **Every picture is drawn from the millimetres in `frames.json`.** A 138 mm
 * frame and a 145 mm frame come out different sizes on the canvas, in the same
 * proportion as they differ in life, and their lenses, bridges and end pieces
 * are all in their recorded places. That matters because the try-on scales the
 * artwork by the frame's physical width: if the picture were generic per shape
 * — as it was — the frame would be drawn at the right overall size with the
 * lenses and bridge in the wrong places inside it, and the wearer's pupils
 * would land somewhere that is not the middle of a lens.
 *
 * Because the geometry is computed rather than eyeballed, the generator also
 * emits the exact calibration for each asset into the manifest: where the
 * frame front starts and ends, where each lens centre is, and where the lens
 * aperture begins and ends. The seeder writes those straight to the colourway,
 * so artwork and data agree by construction and `checkFrameData` has something
 * true to check against.
 */

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const WWWROOT = path.join(HERE, "..", "..", "src", "VisionCart.Web", "wwwroot");
const OUT_DIR = path.join(WWWROOT, "frames");

/**
 * Drawing resolution. Chosen so the widest frame in the catalogue lands near
 * 1200 px across — plenty for a product card and for the try-on canvas without
 * making the PNGs wasteful.
 */
const PX_PER_MM = 7;

/** Room for the arms either side of the frame front, and for the drop shadow. */
const PAD_X_MM = 26;
const PAD_Y_MM = 11;

/**
 * How far a lens outline reaches beyond its nominal half-width and half-height.
 *
 * The outlines are not all inscribed in their box: a cat eye sweeps well above
 * it, an aviator's teardrop dips below it. Boxed lens width and height — the
 * numbers on the arm and in the database — measure the *bounding box*, so the
 * outline has to be shrunk by its own overshoot for the drawn box to come out
 * at the recorded size. Getting this wrong is not cosmetic: the calibration
 * says where the lens aperture is, and the try-on seats the frame on the face
 * with it.
 *
 * Rather than keep a table of overshoots in step with the paths by hand — which
 * is exactly the kind of pair that drifts apart — the overshoot is measured off
 * the path itself, by drawing it at unit size and walking it.
 */
function measureExtents(shape) {
  const d = lensPath(shape, 0, 0, 1, 1, false);
  const box = pathBounds(d);
  return { left: -box.minX, right: box.maxX, top: -box.minY, bottom: box.maxY };
}

/**
 * Bounding box of an SVG path, by sampling every curve.
 *
 * Only the commands these outlines use are handled — M, L, H, V, C, Q, Z, and
 * the elliptical arcs that appear in the rounded rectangles and circles. Arc
 * endpoints alone are enough for those two, because in both cases the arcs are
 * corner rounds or a full ellipse whose extremes are already endpoints of some
 * segment.
 */
function pathBounds(d) {
  const nums = /-?\d*\.?\d+(?:e[-+]?\d+)?/gi;
  const tokens = d.match(/[a-z][^a-z]*/gi) ?? [];

  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  let cx = 0, cy = 0, startX = 0, startY = 0;

  const hit = (x, y) => {
    if (x < minX) minX = x;
    if (x > maxX) maxX = x;
    if (y < minY) minY = y;
    if (y > maxY) maxY = y;
  };

  const SAMPLES = 240;

  for (const token of tokens) {
    const op = token[0];
    const rel = op === op.toLowerCase() && op !== "z" && op !== "Z";
    const a = (token.slice(1).match(nums) ?? []).map(Number);
    const ox = rel ? cx : 0;
    const oy = rel ? cy : 0;

    switch (op.toUpperCase()) {
      case "M":
        cx = a[0] + ox; cy = a[1] + oy; startX = cx; startY = cy; hit(cx, cy);
        break;
      case "L":
        cx = a[0] + ox; cy = a[1] + oy; hit(cx, cy);
        break;
      case "H":
        cx = a[0] + ox; hit(cx, cy);
        break;
      case "V":
        cy = a[0] + oy; hit(cx, cy);
        break;
      case "C": {
        for (let i = 0; i + 5 < a.length; i += 6) {
          const p = [cx, cy, a[i] + ox, a[i + 1] + oy, a[i + 2] + ox, a[i + 3] + oy, a[i + 4] + ox, a[i + 5] + oy];
          for (let s = 0; s <= SAMPLES; s++) {
            const t = s / SAMPLES, u = 1 - t;
            hit(
              u * u * u * p[0] + 3 * u * u * t * p[2] + 3 * u * t * t * p[4] + t * t * t * p[6],
              u * u * u * p[1] + 3 * u * u * t * p[3] + 3 * u * t * t * p[5] + t * t * t * p[7],
            );
          }
          cx = p[6]; cy = p[7];
        }
        break;
      }
      case "Q": {
        for (let i = 0; i + 3 < a.length; i += 4) {
          const p = [cx, cy, a[i] + ox, a[i + 1] + oy, a[i + 2] + ox, a[i + 3] + oy];
          for (let s = 0; s <= SAMPLES; s++) {
            const t = s / SAMPLES, u = 1 - t;
            hit(
              u * u * p[0] + 2 * u * t * p[2] + t * t * p[4],
              u * u * p[1] + 2 * u * t * p[3] + t * t * p[5],
            );
          }
          cx = p[4]; cy = p[5];
        }
        break;
      }
      case "A":
        cx = a[5] + ox; cy = a[6] + oy; hit(cx, cy);
        break;
      case "Z":
        cx = startX; cy = startY;
        break;
      default:
        throw new Error(`unhandled path command "${op}"`);
    }
  }

  return { minX, minY, maxX, maxY };
}

const COLORS = {
  black: { main: "#1b1b1f", light: "#3a3a42", accent: "#0d0d10", label: "Matte Black" },
  tortoise: { main: "#7a4a1e", light: "#c08a3e", accent: "#3d2410", label: "Tortoise" },
  gold: { main: "#c9a227", light: "#f0d97a", accent: "#8a6f14", label: "Brushed Gold" },
  silver: { main: "#9aa3ad", light: "#dfe5eb", accent: "#6e767f", label: "Silver" },
  navy: { main: "#1e2b4a", light: "#3d5488", accent: "#111a2e", label: "Midnight Navy" },
  rose: { main: "#b76e79", light: "#e8b4bc", accent: "#8a4f58", label: "Rose Gold" },
  crystal: { main: "#b9c6d1", light: "#eef4f8", accent: "#8fa0ad", label: "Crystal Clear" },
  olive: { main: "#4a5535", light: "#7d8b5e", accent: "#2f3722", label: "Olive" },
};

/**
 * Turn one frame's millimetres into a drawing plan.
 *
 * Everything the SVG and the calibration need is computed once, here, so the
 * two can never disagree about where the lenses are.
 */
function layout(frame) {
  const { lensWidthMm, lensHeightMm, bridgeWidthMm, totalWidthMm } = frame;

  const endPieceMm = (totalWidthMm - lensWidthMm * 2 - bridgeWidthMm) / 2;
  if (!(endPieceMm >= 0)) {
    throw new Error(
      `${frame.name}: overall width ${totalWidthMm} mm is smaller than two ${lensWidthMm} mm lenses `
      + `plus an ${bridgeWidthMm} mm bridge. One of the three is wrong.`,
    );
  }

  const mm = (v) => v * PX_PER_MM;

  const W = Math.round(mm(totalWidthMm + PAD_X_MM * 2));
  const H = Math.round(mm(lensHeightMm + PAD_Y_MM * 2));

  const frontLeft = mm(PAD_X_MM);
  const frontRight = frontLeft + mm(totalWidthMm);

  const lensW = mm(lensWidthMm);
  const lensH = mm(lensHeightMm);
  const apertureTop = mm(PAD_Y_MM);

  const leftLensLeft = frontLeft + mm(endPieceMm);
  const leftCx = leftLensLeft + lensW / 2;
  const rightCx = leftCx + mm(lensWidthMm + bridgeWidthMm);
  const cy = apertureTop + lensH / 2;

  // The outline is shrunk by its own overshoot so the drawn box comes out at
  // exactly lensWidth x lensHeight.
  const ext = extentsFor(frame.shape);
  const rx = lensW / (ext.left + ext.right);
  const ry = lensH / (ext.top + ext.bottom);

  // An asymmetric outline's centre is not its box centre, so the path is drawn
  // about a point offset from the aperture centre.
  const pathCyLeft = apertureTop + ext.top * ry;
  const pathCxLeft = leftLensLeft + ext.left * rx;
  const pathCxRight = rightCx + (lensW / 2 - ext.left * rx);

  return {
    W, H, rx, ry,
    frontLeft, frontRight,
    leftCx, rightCx, cy,
    apertureTop, apertureBottom: apertureTop + lensH,
    pathCxLeft, pathCxRight, pathCy: pathCyLeft,
    lensW, lensH,
    endPieceMm,
    calibration: {
      leftLensCenterX: round4(leftCx / W),
      leftLensCenterY: round4(cy / H),
      rightLensCenterX: round4(rightCx / W),
      rightLensCenterY: round4(cy / H),
      frontLeftX: round4(frontLeft / W),
      frontRightX: round4(frontRight / W),
      lensTopY: round4(apertureTop / H),
      lensBottomY: round4((apertureTop + lensH) / H),
    },
  };
}

/** Outline of one lens, centred on (cx, cy), as an SVG path. */
function lensPath(shape, cx, cy, rx, ry, mirror) {
  // `s` flips the horizontal asymmetry so the right lens mirrors the left.
  const s = mirror ? -1 : 1;
  const x = (dx) => cx + dx * s;

  switch (shape) {
    case "round":
    case "oval":
      return `M ${cx - rx} ${cy} a ${rx} ${ry} 0 1 0 ${rx * 2} 0 a ${rx} ${ry} 0 1 0 ${-rx * 2} 0 Z`;

    case "aviator": {
      // The teardrop: a wide, nearly flat brow; a small notch by the nose pad;
      // an inner edge falling to a point set toward the nose; and — the part
      // that makes it an aviator rather than a triangle — an outer edge that
      // bulges out to the full half-width on its way back up to the brow.
      const y = (dy) => cy + dy * ry;
      return [
        `M ${x(-rx * 0.96)} ${y(-0.78)}`,
        `C ${x(-rx * 0.55)} ${y(-1.02)} ${x(rx * 0.45)} ${y(-1.02)} ${x(rx * 0.9)} ${y(-0.8)}`,
        // Notch beside the nose pad.
        `C ${x(rx * 1.0)} ${y(-0.7)} ${x(rx * 0.98)} ${y(-0.5)} ${x(rx * 0.86)} ${y(-0.3)}`,
        // Inner edge down to the point, which sits toward the nose, not centre.
        `C ${x(rx * 0.7)} ${y(0.2)} ${x(rx * 0.52)} ${y(0.72)} ${x(rx * 0.22)} ${y(0.94)}`,
        // Bottom sweeping round rather than cutting straight across.
        `C ${x(-rx * 0.05)} ${y(1.04)} ${x(-rx * 0.45)} ${y(0.98)} ${x(-rx * 0.72)} ${y(0.7)}`,
        // Outer edge, bulging to the full width.
        `C ${x(-rx * 0.94)} ${y(0.42)} ${x(-rx * 1.02)} ${y(0.0)} ${x(-rx * 0.96)} ${y(-0.78)}`,
        "Z",
      ].join(" ");
    }

    case "cat_eye": {
      // Outer top corner swept up into a point, bottom kept shallow.
      const y = (dy) => cy + dy * ry;
      return [
        `M ${x(-rx * 1.02)} ${y(-1.18)}`,
        // Brow sloping down from the swept outer tip to the nose.
        `C ${x(-rx * 0.25)} ${y(-1.0)} ${x(rx * 0.5)} ${y(-0.92)} ${x(rx * 0.98)} ${y(-0.62)}`,
        `C ${x(rx * 1.06)} ${y(-0.16)} ${x(rx * 1.0)} ${y(0.3)} ${x(rx * 0.72)} ${y(0.62)}`,
        // Broad, shallow bottom — a deep arc here reads as a half-moon.
        `C ${x(rx * 0.3)} ${y(0.96)} ${x(-rx * 0.35)} ${y(0.96)} ${x(-rx * 0.76)} ${y(0.62)}`,
        // Outer edge rising into the tip.
        `C ${x(-rx * 0.99)} ${y(0.28)} ${x(-rx * 1.02)} ${y(-0.42)} ${x(-rx * 1.02)} ${y(-1.18)}`,
        "Z",
      ].join(" ");
    }

    case "wayfarer":
      // Wider at the brow than the jaw, with softened corners.
      return [
        `M ${x(-rx)} ${cy - ry * 0.85}`,
        `Q ${x(-rx)} ${cy - ry} ${x(-rx * 0.78)} ${cy - ry}`,
        `L ${x(rx * 0.86)} ${cy - ry}`,
        `Q ${x(rx)} ${cy - ry} ${x(rx)} ${cy - ry * 0.78}`,
        `L ${x(rx * 0.82)} ${cy + ry * 0.72}`,
        `Q ${x(rx * 0.78)} ${cy + ry} ${x(rx * 0.5)} ${cy + ry}`,
        `L ${x(-rx * 0.6)} ${cy + ry}`,
        `Q ${x(-rx * 0.9)} ${cy + ry} ${x(-rx * 0.95)} ${cy + ry * 0.6}`,
        "Z",
      ].join(" ");

    case "geometric":
      return [
        `M ${x(-rx)} ${cy - ry * 0.35}`,
        `L ${x(-rx * 0.45)} ${cy - ry}`,
        `L ${x(rx * 0.55)} ${cy - ry}`,
        `L ${x(rx)} ${cy - ry * 0.3}`,
        `L ${x(rx * 0.6)} ${cy + ry}`,
        `L ${x(-rx * 0.5)} ${cy + ry}`,
        "Z",
      ].join(" ");

    case "square":
      return roundedRect(cx - rx, cy - ry, rx * 2, ry * 2, Math.min(18, ry * 0.2));

    case "browline":
    case "rectangle":
    default:
      return roundedRect(
        cx - rx, cy - ry, rx * 2, ry * 2,
        Math.min(shape === "browline" ? 26 : 34, ry * 0.35),
      );
  }
}

function roundedRect(x, y, w, h, r) {
  return [
    `M ${x + r} ${y}`,
    `H ${x + w - r}`,
    `A ${r} ${r} 0 0 1 ${x + w} ${y + r}`,
    `V ${y + h - r}`,
    `A ${r} ${r} 0 0 1 ${x + w - r} ${y + h}`,
    `H ${x + r}`,
    `A ${r} ${r} 0 0 1 ${x} ${y + h - r}`,
    `V ${y + r}`,
    `A ${r} ${r} 0 0 1 ${x + r} ${y}`,
    "Z",
  ].join(" ");
}

function buildSvg(frame, colorKey, plan) {
  const c = COLORS[colorKey];
  const { W, H, rx, ry, cy, leftCx, rightCx, frontLeft, frontRight } = plan;
  const rimless = frame.rimType === "rimless";
  const semi = frame.rimType === "semi_rimless";
  const tinted = Boolean(frame.tint);

  // Rim thickness scales with the frame, so a small frame does not come out
  // looking like it was made from scaffolding.
  const unit = plan.lensH / 100;
  const strokeW = rimless ? 0 : Math.round((frame.shape === "round" || frame.shape === "oval" ? 4.6 : 5.8) * unit);

  const left = lensPath(frame.shape, plan.pathCxLeft, plan.pathCy, rx, ry, false);
  const right = lensPath(frame.shape, plan.pathCxRight, plan.pathCy, rx, ry, true);

  // The bridge spans the recorded gap between the two lenses, at the height a
  // real one sits: about a third of the way down the lens.
  const bridgeY = cy - plan.lensH * 0.21;
  // The bridge spans exactly the recorded gap between the two lenses — it IS
  // bridgeWidthMm. Overlapping it into the rims would draw a frame whose
  // measurements the picture contradicts.
  const bridgeInnerL = leftCx + plan.lensW / 2;
  const bridgeInnerR = rightCx - plan.lensW / 2;
  const bridgeLift = frame.shape === "aviator" ? -13 * unit : -7 * unit;
  const bridge = `M ${bridgeInnerL} ${bridgeY} q ${(bridgeInnerR - bridgeInnerL) / 2} ${bridgeLift} ${bridgeInnerR - bridgeInnerL} 0`;

  // Temple arms. They start at the outer edge of the lens, cross the end piece
  // to the hinge at the edge of the frame front, and run back toward the ear.
  // Starting them at the hinge instead would leave the end piece undrawn and
  // the arms floating clear of the frame — which is what the recorded overall
  // width, larger than two lenses and a bridge, is telling us is there.
  const templeY = cy - plan.lensH * 0.25;
  const lensOuterL = leftCx - plan.lensW / 2;
  const lensOuterR = rightCx + plan.lensW / 2;
  const tipInset = W * 0.034;
  const leftTemple = `M ${lensOuterL} ${templeY} L ${tipInset} ${templeY - 4 * unit} q ${-10 * unit} ${unit} ${-10 * unit} ${6 * unit}`;
  const rightTemple = `M ${lensOuterR} ${templeY} L ${W - tipInset} ${templeY - 4 * unit} q ${10 * unit} ${unit} ${10 * unit} ${6 * unit}`;

  // The hinge block, sitting where the front ends and the arm begins.
  const hinge = Math.round(7 * unit);
  const hinges = `<path d="M ${frontLeft} ${templeY}" stroke="url(#g)" stroke-width="${hinge}" stroke-linecap="round" fill="none"/>`
    + `<path d="M ${frontRight} ${templeY}" stroke="url(#g)" stroke-width="${hinge}" stroke-linecap="round" fill="none"/>`;

  // The heavy brow is the whole point of a browline frame.
  const browBar = frame.shape === "browline"
    ? `<path d="M ${frontLeft + plan.lensW * 0.04} ${plan.apertureTop + 3 * unit} L ${frontRight - plan.lensW * 0.04} ${plan.apertureTop + 3 * unit}"
           stroke="url(#g)" stroke-width="${Math.round(17 * unit)}" stroke-linecap="round" fill="none" />`
    : "";

  const lensFill = tinted
    ? `<path d="${left}" fill="${frame.tint}" opacity="0.55"/><path d="${right}" fill="${frame.tint}" opacity="0.55"/>`
    : `<path d="${left}" fill="#ffffff" opacity="0.07"/><path d="${right}" fill="#ffffff" opacity="0.07"/>`;

  const rimClip = semi
    ? `<clipPath id="halfRim"><rect x="0" y="0" width="${W}" height="${cy + plan.lensH * 0.05}"/></clipPath>`
    : "";
  const rimGroupAttr = semi ? ' clip-path="url(#halfRim)"' : "";

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="${c.light}"/>
      <stop offset="55%" stop-color="${c.main}"/>
      <stop offset="100%" stop-color="${c.accent}"/>
    </linearGradient>
    <linearGradient id="glare" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#ffffff" stop-opacity="0.45"/>
      <stop offset="42%" stop-color="#ffffff" stop-opacity="0.06"/>
      <stop offset="100%" stop-color="#ffffff" stop-opacity="0"/>
    </linearGradient>
    <filter id="soft" x="-10%" y="-20%" width="120%" height="140%">
      <feDropShadow dx="0" dy="${1.2 * unit}" stdDeviation="${1.6 * unit}" flood-color="#000" flood-opacity="0.28"/>
    </filter>
    ${rimClip}
  </defs>

  <g filter="url(#soft)">
    ${lensFill}
    <path d="${left}" fill="url(#glare)" opacity="0.35"/>
    <path d="${right}" fill="url(#glare)" opacity="0.35"/>

    <path d="${leftTemple}" stroke="url(#g)" stroke-width="${Math.round(5 * unit)}" fill="none" stroke-linecap="round"/>
    <path d="${rightTemple}" stroke="url(#g)" stroke-width="${Math.round(5 * unit)}" fill="none" stroke-linecap="round"/>
    <path d="${bridge}" stroke="url(#g)" stroke-width="${Math.round(5 * unit)}" fill="none" stroke-linecap="round"/>
    ${hinges}
    ${browBar}

    <g${rimGroupAttr}>
      <path d="${left}" fill="none" stroke="url(#g)" stroke-width="${strokeW}" stroke-linejoin="round"/>
      <path d="${right}" fill="none" stroke="url(#g)" stroke-width="${strokeW}" stroke-linejoin="round"/>
    </g>
  </g>
</svg>`;
}

/**
 * The unit-size overshoot of one shape's outline, measured once and cached.
 *
 * A circle or an ellipse drawn as two arcs has its extremes mid-segment rather
 * than at an endpoint, so those two are stated analytically; every other
 * outline is measured.
 */
const EXTENT_CACHE = new Map();

function extentsFor(shape) {
  if (!EXTENT_CACHE.has(shape)) {
    EXTENT_CACHE.set(
      shape,
      shape === "round" || shape === "oval"
        ? { left: 1, right: 1, top: 1, bottom: 1 }
        : measureExtents(shape),
    );
  }
  return EXTENT_CACHE.get(shape);
}

function round4(v) {
  return Math.round(v * 10000) / 10000;
}

async function main() {
  const source = JSON.parse(await fs.readFile(path.join(HERE, "frames.json"), "utf8"));
  await fs.mkdir(OUT_DIR, { recursive: true });

  const manifest = [];

  for (const frame of source.frames) {
    const plan = layout(frame);

    for (const colorKey of frame.colors) {
      const key = `${frame.name.toLowerCase()}-${colorKey}`;
      const svg = buildSvg(frame, colorKey, plan);

      await sharp(Buffer.from(svg))
        .png({ compressionLevel: 9 })
        .toFile(path.join(OUT_DIR, `${key}.png`));

      manifest.push({
        key,
        frame: frame.name,
        shape: frame.shape,
        rimType: frame.rimType,
        color: colorKey,
        colorLabel: COLORS[colorKey].label,
        colorHex: COLORS[colorKey].main,
        tinted: Boolean(frame.tint),
        url: `/frames/${key}.png`,
        imageWidth: plan.W,
        imageHeight: plan.H,
        lensWidthMm: frame.lensWidthMm,
        bridgeWidthMm: frame.bridgeWidthMm,
        templeLengthMm: frame.templeLengthMm,
        lensHeightMm: frame.lensHeightMm,
        totalWidthMm: frame.totalWidthMm,
        calibration: plan.calibration,
      });
    }

    const c = plan.calibration;
    const ppmCentres = ((c.rightLensCenterX - c.leftLensCenterX) * plan.W) / (frame.lensWidthMm + frame.bridgeWidthMm);
    const ppmFront = ((c.frontRightX - c.frontLeftX) * plan.W) / frame.totalWidthMm;
    const ppmHeight = ((c.lensBottomY - c.lensTopY) * plan.H) / frame.lensHeightMm;
    const spread = (Math.max(ppmCentres, ppmFront, ppmHeight) - Math.min(ppmCentres, ppmFront, ppmHeight))
      / Math.max(ppmCentres, ppmFront, ppmHeight);

    console.log(
      `  ${frame.name.padEnd(8)} ${plan.W}x${plan.H}px  `
      + `end pieces ${plan.endPieceMm.toFixed(1)}mm  `
      + `scale agreement ${(100 - spread * 100).toFixed(2)}%`,
    );
  }

  await fs.writeFile(
    path.join(OUT_DIR, "manifest.json"),
    JSON.stringify(manifest, null, 2),
    "utf8",
  );
  console.log(`\n✓ generated ${manifest.length} frame images in wwwroot/frames`);
}

await main();
