#!/usr/bin/env node
/**
 * Draws the frame artwork the catalogue and the virtual try-on both use.
 *
 * Real product photography replaces these later — upload a transparent PNG in
 * the back office and it takes over. Until then this gives a complete, working
 * shop on a fresh install, with artwork whose anchor points are exact by
 * construction rather than eyeballed.
 *
 * Geometry contract: the canvas is 1000x420 and the two lens centres sit at
 * x = 290 and x = 710, y = 210 — which is 0.29 / 0.71 / 0.50 normalised, the
 * DEFAULT_ANCHORS in ClientApp/tryon/geometry.ts. Change one, change the other.
 */

import fs from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";

const W = 1000;
const H = 420;
const LEFT_CX = 290;
const RIGHT_CX = 710;
const CY = 210;

import { fileURLToPath } from "node:url";

/** This file now lives in the .NET project; wwwroot is the static root. */
const HERE = path.dirname(fileURLToPath(import.meta.url));
const WWWROOT = path.join(HERE, "..", "..", "src", "VisionCart.Web", "wwwroot");

const OUT_DIR = path.join(WWWROOT, "frames");

/** Half-width and half-height of one lens, per shape. */
const SHAPES = {
  rectangle: { rx: 148, ry: 96 },
  square: { rx: 140, ry: 122 },
  round: { rx: 130, ry: 130 },
  oval: { rx: 150, ry: 105 },
  aviator: { rx: 143, ry: 126 },
  cat_eye: { rx: 146, ry: 108 },
  wayfarer: { rx: 150, ry: 110 },
  geometric: { rx: 142, ry: 112 },
  browline: { rx: 145, ry: 105 },
};

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

/** Outline of one lens, centred on (cx, cy), as an SVG path. */
function lensPath(shape, cx, cy, mirror) {
  const { rx, ry } = SHAPES[shape];
  // `s` flips the horizontal asymmetry so the right lens mirrors the left.
  const s = mirror ? -1 : 1;
  const x = (dx) => cx + dx * s;

  switch (shape) {
    case "round":
      return `M ${cx - rx} ${cy} a ${rx} ${ry} 0 1 0 ${rx * 2} 0 a ${rx} ${ry} 0 1 0 ${-rx * 2} 0 Z`;

    case "oval":
      return `M ${cx - rx} ${cy} a ${rx} ${ry} 0 1 0 ${rx * 2} 0 a ${rx} ${ry} 0 1 0 ${-rx * 2} 0 Z`;

    case "aviator": {
      // Teardrop: near-straight brow, outer edge falling away, bottom pulled
      // to a soft point just inside centre. +dx is toward the nose.
      const y = (dy) => cy + dy * ry;
      return [
        `M ${x(-rx * 0.98)} ${y(-0.82)}`,
        // Brow: wide and nearly flat, with the small notch by the nose pad.
        `C ${x(-rx * 0.45)} ${y(-1.04)} ${x(rx * 0.5)} ${y(-1.04)} ${x(rx * 0.92)} ${y(-0.86)}`,
        `C ${x(rx * 1.02)} ${y(-0.78)} ${x(rx * 1.0)} ${y(-0.62)} ${x(rx * 0.86)} ${y(-0.42)}`,
        // Inner edge sweeping down to the teardrop point.
        `C ${x(rx * 0.66)} ${y(0.18)} ${x(rx * 0.4)} ${y(0.72)} ${x(rx * 0.06)} ${y(0.98)}`,
        `C ${x(-rx * 0.16)} ${y(1.06)} ${x(-rx * 0.42)} ${y(0.96)} ${x(-rx * 0.62)} ${y(0.7)}`,
        // Outer edge climbing back to the brow.
        `C ${x(-rx * 0.9)} ${y(0.3)} ${x(-rx * 1.0)} ${y(-0.24)} ${x(-rx * 0.98)} ${y(-0.82)}`,
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
      return roundedRect(cx - rx, cy - ry, rx * 2, ry * 2, 18);

    case "browline":
    case "rectangle":
    default:
      return roundedRect(cx - rx, cy - ry, rx * 2, ry * 2, shape === "browline" ? 26 : 34);
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

function buildSvg(shape, colorKey, opts = {}) {
  const c = COLORS[colorKey];
  const { rx, ry } = SHAPES[shape];
  const rimless = opts.rimType === "rimless";
  const semi = opts.rimType === "semi_rimless";
  const tinted = Boolean(opts.tint);

  const strokeW = rimless ? 0 : shape === "round" || shape === "oval" ? 12 : 15;
  const left = lensPath(shape, LEFT_CX, CY, false);
  const right = lensPath(shape, RIGHT_CX, CY, true);

  // Bridge sits at the top third of the lens, as it does on a real frame.
  const bridgeY = CY - ry * 0.42;
  const bridgeInnerL = LEFT_CX + rx * 0.92;
  const bridgeInnerR = RIGHT_CX - rx * 0.92;
  const bridge =
    shape === "aviator"
      ? `M ${bridgeInnerL} ${bridgeY} q ${(bridgeInnerR - bridgeInnerL) / 2} ${-34} ${bridgeInnerR - bridgeInnerL} 0`
      : `M ${bridgeInnerL} ${bridgeY} q ${(bridgeInnerR - bridgeInnerL) / 2} ${-18} ${bridgeInnerR - bridgeInnerL} 0`;

  // Temple arms disappearing behind the ears.
  const templeY = CY - ry * 0.5;
  const leftTemple = `M ${LEFT_CX - rx} ${templeY} L 34 ${templeY - 10} q -26 2 -26 16`;
  const rightTemple = `M ${RIGHT_CX + rx} ${templeY} L ${W - 34} ${templeY - 10} q 26 2 26 16`;

  // The heavy brow is the whole point of a browline frame, so it is drawn as a
  // thick bar sitting just over the top of both rims.
  const browBar =
    shape === "browline"
      ? `<path d="M ${LEFT_CX - rx - 8} ${CY - ry + 8} L ${RIGHT_CX + rx + 8} ${CY - ry + 8}"
             stroke="url(#g)" stroke-width="42" stroke-linecap="round" fill="none" />`
      : "";

  const lensFill = tinted
    ? `<path d="${left}" fill="${opts.tint}" opacity="0.55"/><path d="${right}" fill="${opts.tint}" opacity="0.55"/>`
    : `<path d="${left}" fill="#ffffff" opacity="0.07"/><path d="${right}" fill="#ffffff" opacity="0.07"/>`;

  // Semi-rimless: only the top half of the rim is drawn.
  const rimClip = semi
    ? `<clipPath id="halfRim"><rect x="0" y="0" width="${W}" height="${CY + ry * 0.1}"/></clipPath>`
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
      <feDropShadow dx="0" dy="3" stdDeviation="4" flood-color="#000" flood-opacity="0.28"/>
    </filter>
    ${rimClip}
  </defs>

  <g filter="url(#soft)">
    ${lensFill}
    <path d="${left}" fill="url(#glare)" opacity="0.35"/>
    <path d="${right}" fill="url(#glare)" opacity="0.35"/>

    <path d="${leftTemple}" stroke="url(#g)" stroke-width="13" fill="none" stroke-linecap="round"/>
    <path d="${rightTemple}" stroke="url(#g)" stroke-width="13" fill="none" stroke-linecap="round"/>
    <path d="${bridge}" stroke="url(#g)" stroke-width="13" fill="none" stroke-linecap="round"/>
    ${browBar}

    <g${rimGroupAttr}>
      <path d="${left}" fill="none" stroke="url(#g)" stroke-width="${strokeW}" stroke-linejoin="round"/>
      <path d="${right}" fill="none" stroke="url(#g)" stroke-width="${strokeW}" stroke-linejoin="round"/>
    </g>
  </g>
</svg>`;
}

async function main() {
  await fs.mkdir(OUT_DIR, { recursive: true });
  const manifest = [];

  const combos = [
    { shape: "rectangle", rimType: "full_rim", colors: ["black", "tortoise", "navy", "crystal"] },
    { shape: "round", rimType: "full_rim", colors: ["gold", "black", "rose", "silver"] },
    { shape: "cat_eye", rimType: "full_rim", colors: ["tortoise", "black", "rose"] },
    { shape: "aviator", rimType: "full_rim", colors: ["gold", "silver", "black"], tint: "#3a3f4a" },
    { shape: "wayfarer", rimType: "full_rim", colors: ["black", "tortoise", "navy"] },
    { shape: "square", rimType: "full_rim", colors: ["black", "crystal", "olive"] },
    { shape: "oval", rimType: "semi_rimless", colors: ["silver", "gold"] },
    { shape: "geometric", rimType: "full_rim", colors: ["navy", "olive", "gold"] },
    { shape: "browline", rimType: "semi_rimless", colors: ["tortoise", "black", "silver"] },
    { shape: "rectangle", rimType: "rimless", colors: ["silver", "gold"], suffix: "rimless" },
  ];

  for (const combo of combos) {
    for (const colorKey of combo.colors) {
      const name = [combo.shape, combo.suffix, colorKey].filter(Boolean).join("-");
      const svg = buildSvg(combo.shape, colorKey, {
        rimType: combo.rimType,
        tint: combo.tint,
      });

      const file = path.join(OUT_DIR, `${name}.png`);
      await sharp(Buffer.from(svg)).png({ compressionLevel: 9 }).toFile(file);

      manifest.push({
        key: name,
        shape: combo.shape,
        rimType: combo.rimType,
        color: colorKey,
        colorLabel: COLORS[colorKey].label,
        colorHex: COLORS[colorKey].main,
        tinted: Boolean(combo.tint),
        url: `/frames/${name}.png`,
      });
    }
  }

  await fs.writeFile(
    path.join(OUT_DIR, "manifest.json"),
    JSON.stringify(manifest, null, 2),
    "utf8",
  );
  console.log(`✓ generated ${manifest.length} frame images in wwwroot/frames`);
}

await main();
