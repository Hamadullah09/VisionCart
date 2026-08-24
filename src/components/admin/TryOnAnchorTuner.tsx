"use client";

import { useActionState, useEffect, useRef, useState } from "react";
import { saveVariantAction, type AdminResult } from "@/app/actions/admin";
import BulkUploader from "./BulkUploader";
import { drawFrame, solveTransform, type FrameAnchors } from "@/lib/tryon";
import type { EditableVariant } from "./VariantEditor";

/**
 * Calibrates a frame overlay for the virtual mirror.
 *
 * Staff drag two markers onto the points in the artwork where the wearer's
 * pupils should sit. That is the entire calibration — the try-on solves scale,
 * rotation and position from those two points against the customer's detected
 * pupils. The preview beside it shows the result on a reference face
 * immediately, so a bad anchor is obvious before it reaches the shop.
 */

const PREVIEW_W = 520;
const PREVIEW_H = 380;
// Reference pupils on the mock face, in preview pixels.
const REF_LEFT = { x: PREVIEW_W * 0.37, y: PREVIEW_H * 0.46 };
const REF_RIGHT = { x: PREVIEW_W * 0.63, y: PREVIEW_H * 0.46 };

export default function TryOnAnchorTuner({
  variantId,
  frameId,
  overlayUrl,
  anchors: initialAnchors,
  opacity: initialOpacity,
  variant,
}: {
  variantId: string;
  frameId: string;
  overlayUrl: string | null;
  anchors: FrameAnchors;
  opacity: number;
  variant: EditableVariant;
}) {
  const [state, action, pending] = useActionState<AdminResult | null, FormData>(
    saveVariantAction,
    null,
  );

  const [anchors, setAnchors] = useState<FrameAnchors>(initialAnchors);
  const [opacity, setOpacity] = useState(initialOpacity);
  const imgRef = useRef<HTMLImageElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const dragRef = useRef<"left" | "right" | null>(null);
  const [loaded, setLoaded] = useState(false);

  // Load the overlay once so the preview can draw it.
  useEffect(() => {
    if (!overlayUrl) return;
    const img = new Image();
    img.onload = () => {
      imgRef.current = img;
      setLoaded(true);
    };
    img.src = overlayUrl;
  }, [overlayUrl]);

  // Redraw the reference face whenever a knob moves.
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    ctx.clearRect(0, 0, PREVIEW_W, PREVIEW_H);
    drawReferenceFace(ctx);

    const img = imgRef.current;
    if (!img) return;

    const t = solveTransform({
      leftPupil: REF_LEFT,
      rightPupil: REF_RIGHT,
      assetWidth: img.naturalWidth,
      assetHeight: img.naturalHeight,
      anchors,
    });
    drawFrame(ctx, img, t, {
      width: img.naturalWidth,
      height: img.naturalHeight,
      opacity,
    });
  }, [anchors, opacity, loaded]);

  function beginDrag(which: "left" | "right", e: React.PointerEvent) {
    dragRef.current = which;
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
  }

  function onPointerMove(e: React.PointerEvent) {
    const which = dragRef.current;
    if (!which) return;

    // currentTarget is the stage itself, so the drag maths never needs to read
    // a ref — and the handler stays safe for the React compiler to optimise.
    const rect = e.currentTarget.getBoundingClientRect();
    const x = clamp01((e.clientX - rect.left) / rect.width);
    const y = clamp01((e.clientY - rect.top) / rect.height);

    setAnchors((a) =>
      which === "left" ? { ...a, leftX: x, leftY: y } : { ...a, rightX: x, rightY: y },
    );
  }

  if (!overlayUrl) {
    return (
      <div className="space-y-4">
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
          This colourway has no try-on artwork yet, so it won&apos;t appear in the virtual mirror.
          Upload a front-on PNG with a transparent background.
        </p>
        <BulkUploader
          variantId={variantId}
          role="try_on"
          label="Drop the try-on PNG here"
          hint="Transparent background, frame shot straight on, temples pointing outward."
        />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <div className="grid gap-5 lg:grid-cols-2">
        {/* Anchor placement */}
        <div>
          <h4 className="text-sm font-semibold">Where should the pupils sit?</h4>
          <p className="mt-0.5 text-xs text-ink-500">
            Drag each marker to the centre of the corresponding lens.
          </p>

          <div
            onPointerMove={onPointerMove}
            onPointerUp={() => (dragRef.current = null)}
            className="relative mt-3 touch-none rounded-xl border border-ink-200 bg-[repeating-conic-gradient(#f1f3f6_0_25%,#fff_0_50%)] bg-[length:20px_20px]"
          >
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={overlayUrl} alt="Try-on artwork" className="block w-full" draggable={false} />

            <Marker which="left" x={anchors.leftX} y={anchors.leftY} label="L" onGrab={beginDrag} />
            <Marker
              which="right"
              x={anchors.rightX}
              y={anchors.rightY}
              label="R"
              onGrab={beginDrag}
            />
          </div>

          <div className="mt-3 grid grid-cols-2 gap-2 text-xs text-ink-500">
            <span>
              Left: {anchors.leftX.toFixed(3)}, {anchors.leftY.toFixed(3)}
            </span>
            <span>
              Right: {anchors.rightX.toFixed(3)}, {anchors.rightY.toFixed(3)}
            </span>
          </div>

          <button
            type="button"
            onClick={() =>
              setAnchors({ leftX: 0.29, leftY: 0.5, rightX: 0.71, rightY: 0.5, scaleAdj: 1 })
            }
            className="mt-2 text-xs text-ink-500 underline underline-offset-2"
          >
            Reset to defaults
          </button>
        </div>

        {/* Live preview */}
        <div>
          <h4 className="text-sm font-semibold">On a reference face</h4>
          <p className="mt-0.5 text-xs text-ink-500">
            A 63 mm PD, straight on. This is what the customer will see.
          </p>
          <canvas
            ref={canvasRef}
            width={PREVIEW_W}
            height={PREVIEW_H}
            className="mt-3 w-full rounded-xl border border-ink-200 bg-white"
          />

          <div className="mt-3 space-y-3">
            <Slider
              label={`Size adjustment (${anchors.scaleAdj.toFixed(2)}×)`}
              min={0.7}
              max={1.4}
              step={0.01}
              value={anchors.scaleAdj}
              onChange={(scaleAdj) => setAnchors((a) => ({ ...a, scaleAdj }))}
            />
            <Slider
              label={`Lens opacity (${Math.round(opacity * 100)}%)`}
              min={0.3}
              max={1}
              step={0.01}
              value={opacity}
              onChange={setOpacity}
            />
          </div>
        </div>
      </div>

      {/* Save — carries the whole variant so a calibration save doesn't wipe
          the details fields. */}
      <form action={action} className="flex flex-wrap items-center gap-3">
        <input type="hidden" name="id" value={variantId} />
        <input type="hidden" name="frameId" value={frameId} />
        <input type="hidden" name="sku" value={variant.sku} />
        <input type="hidden" name="colorName" value={variant.colorName} />
        <input type="hidden" name="colorHex" value={variant.colorHex ?? ""} />
        <input type="hidden" name="barcode" value={variant.barcode ?? ""} />
        <input type="hidden" name="price" value={variant.priceMajor} />
        <input type="hidden" name="stockQty" value={variant.stockQty} />
        <input type="hidden" name="lowStockAt" value={variant.lowStockAt} />
        <input type="hidden" name="position" value={variant.position} />
        {variant.isActive && <input type="hidden" name="isActive" value="on" />}

        <input type="hidden" name="anchorLeftX" value={anchors.leftX} />
        <input type="hidden" name="anchorLeftY" value={anchors.leftY} />
        <input type="hidden" name="anchorRightX" value={anchors.rightX} />
        <input type="hidden" name="anchorRightY" value={anchors.rightY} />
        <input type="hidden" name="tryOnScaleAdj" value={anchors.scaleAdj} />
        <input type="hidden" name="tryOnOpacity" value={opacity} />

        <button type="submit" disabled={pending} className="btn-primary btn-sm">
          {pending ? "Saving…" : "Save calibration"}
        </button>

        {state && !state.ok && <span className="text-sm text-rose-700">{state.error}</span>}
        {state?.ok && <span className="text-sm text-emerald-700">Calibration saved.</span>}
      </form>

      <details className="text-sm">
        <summary className="cursor-pointer font-medium">Replace the artwork</summary>
        <div className="mt-3">
          <BulkUploader
            variantId={variantId}
            role="try_on"
            label="Drop a replacement PNG"
            hint="Transparent background. Re-check the anchors afterwards."
          />
        </div>
      </details>
    </div>
  );
}

function Marker({
  which,
  x,
  y,
  label,
  onGrab,
}: {
  which: "left" | "right";
  x: number;
  y: number;
  label: string;
  onGrab: (which: "left" | "right", e: React.PointerEvent) => void;
}) {
  return (
    <button
      type="button"
      onPointerDown={(e) => onGrab(which, e)}
      aria-label={`${label} pupil anchor`}
      style={{ left: `${x * 100}%`, top: `${y * 100}%` }}
      className="absolute grid h-7 w-7 -translate-x-1/2 -translate-y-1/2 cursor-grab place-items-center rounded-full border-2 border-white bg-brand-600 text-[11px] font-bold text-white shadow active:cursor-grabbing"
    >
      {label}
    </button>
  );
}

function Slider({
  label,
  min,
  max,
  step,
  value,
  onChange,
}: {
  label: string;
  min: number;
  max: number;
  step: number;
  value: number;
  onChange: (v: number) => void;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-ink-600">{label}</span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="w-full"
      />
    </label>
  );
}

/** A neutral head so the preview has something to sit on. Deliberately plain. */
function drawReferenceFace(ctx: CanvasRenderingContext2D) {
  const cx = PREVIEW_W / 2;

  ctx.fillStyle = "#f8fafc";
  ctx.fillRect(0, 0, PREVIEW_W, PREVIEW_H);

  ctx.fillStyle = "#e7d7c9";
  ctx.beginPath();
  ctx.ellipse(cx, PREVIEW_H * 0.52, PREVIEW_W * 0.21, PREVIEW_H * 0.36, 0, 0, Math.PI * 2);
  ctx.fill();

  // Brows
  ctx.strokeStyle = "#8a7263";
  ctx.lineWidth = 6;
  ctx.lineCap = "round";
  for (const p of [REF_LEFT, REF_RIGHT]) {
    ctx.beginPath();
    ctx.moveTo(p.x - 26, p.y - 26);
    ctx.quadraticCurveTo(p.x, p.y - 36, p.x + 26, p.y - 26);
    ctx.stroke();
  }

  // Eyes
  for (const p of [REF_LEFT, REF_RIGHT]) {
    ctx.fillStyle = "#ffffff";
    ctx.beginPath();
    ctx.ellipse(p.x, p.y, 20, 11, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = "#4a3b2f";
    ctx.beginPath();
    ctx.arc(p.x, p.y, 8, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = "#111";
    ctx.beginPath();
    ctx.arc(p.x, p.y, 3.5, 0, Math.PI * 2);
    ctx.fill();
  }

  // Nose and mouth, just enough for a sense of scale
  ctx.strokeStyle = "#c9ab97";
  ctx.lineWidth = 4;
  ctx.beginPath();
  ctx.moveTo(cx, REF_LEFT.y + 12);
  ctx.lineTo(cx - 8, REF_LEFT.y + 62);
  ctx.quadraticCurveTo(cx, REF_LEFT.y + 72, cx + 8, REF_LEFT.y + 62);
  ctx.stroke();

  ctx.beginPath();
  ctx.moveTo(cx - 24, REF_LEFT.y + 104);
  ctx.quadraticCurveTo(cx, REF_LEFT.y + 118, cx + 24, REF_LEFT.y + 104);
  ctx.stroke();
}

function clamp01(v: number) {
  return Math.max(0, Math.min(1, v));
}
