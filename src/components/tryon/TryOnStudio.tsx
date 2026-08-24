"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useFaceLandmarker } from "./useFaceLandmarker";
import type { TryOnFrame } from "./types";
import {
  DEFAULT_ANCHORS,
  NO_ADJUSTMENT,
  drawFrame,
  estimateFaceShape,
  fitContain,
  fitCover,
  measureFace,
  measurementAdvice,
  solveTransform,
  suggestSizeBand,
  type Adjustment,
  type FaceMeasurement,
  type Point,
} from "@/lib/tryon";
import { formatMoney } from "@/lib/money";

/**
 * The virtual mirror.
 *
 * Two ways in: upload a photo, or open the camera. Both end up in the same
 * place — two pupil points on a canvas — after which the frame overlay is
 * placed by pure geometry. Face detection is a convenience; if it is
 * unavailable or the photo defeats it, the customer drags two markers onto
 * their eyes and everything downstream works identically.
 *
 * Nothing is uploaded. The photo and every rendered frame stay inside the
 * browser until the customer presses "Save to my file".
 */

type Mode = "upload" | "camera";
const CANVAS_W = 900;
const CANVAS_H = 675;

export default function TryOnStudio({
  frames,
  initialVariantId,
  canSave,
  cameraEnabled = true,
  compact = false,
}: {
  frames: TryOnFrame[];
  initialVariantId?: string;
  /** Saving needs both a signed-in customer and the store setting enabled. */
  canSave: boolean;
  cameraEnabled?: boolean;
  compact?: boolean;
}) {
  const [mode, setMode] = useState<Mode>("upload");
  const [selectedId, setSelectedId] = useState<string>(
    initialVariantId ?? frames[0]?.variantId ?? "",
  );
  const [adjust, setAdjust] = useState<Adjustment>(NO_ADJUSTMENT);
  const [manual, setManual] = useState(false);
  const [pupils, setPupils] = useState<{ a: Point; b: Point } | null>(null);
  const [measurement, setMeasurement] = useState<FaceMeasurement | null>(null);
  const [faceShape, setFaceShape] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  const [showGuides, setShowGuides] = useState(false);
  // Mirrors photoRef so the render pass never has to read a ref to decide
  // whether there is a subject on the canvas.
  const [hasPhoto, setHasPhoto] = useState(false);
  const [overlayLoads, setOverlayLoads] = useState(0);

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const photoRef = useRef<HTMLImageElement | null>(null);
  const overlayRef = useRef<HTMLImageElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const rafRef = useRef<number>(0);
  const draggingRef = useRef<"a" | "b" | null>(null);

  const { status: detectStatus, detectImage, detectVideo } = useFaceLandmarker(true);

  const selected = useMemo(
    () => frames.find((f) => f.variantId === selectedId) ?? frames[0] ?? null,
    [frames, selectedId],
  );

  // --- Rendering ----------------------------------------------------------
  const renderOnce = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    ctx.clearRect(0, 0, CANVAS_W, CANVAS_H);
    ctx.fillStyle = "#0b0f14";
    ctx.fillRect(0, 0, CANVAS_W, CANVAS_H);

    // 1. The person
    let drew = false;
    if (mode === "camera" && videoRef.current && videoRef.current.readyState >= 2) {
      const v = videoRef.current;
      const box = fitCover(v.videoWidth, v.videoHeight, CANVAS_W, CANVAS_H);
      ctx.save();
      // Mirror so it behaves like a real mirror rather than a video call.
      ctx.translate(CANVAS_W, 0);
      ctx.scale(-1, 1);
      ctx.drawImage(v, CANVAS_W - box.x - box.width, box.y, box.width, box.height);
      ctx.restore();
      drew = true;
    } else if (photoRef.current) {
      const p = photoRef.current;
      const box = fitContain(p.naturalWidth, p.naturalHeight, CANVAS_W, CANVAS_H);
      ctx.drawImage(p, box.x, box.y, box.width, box.height);
      drew = true;
    }

    if (!drew) {
      ctx.fillStyle = "#5b6b7d";
      ctx.font = "500 22px system-ui, sans-serif";
      ctx.textAlign = "center";
      ctx.fillText("Upload a photo or start the camera", CANVAS_W / 2, CANVAS_H / 2);
      return;
    }

    // 2. The frame
    const overlay = overlayRef.current;
    if (overlay && pupils) {
      // Order by x so the artwork never lands mirrored, whatever the source.
      const [left, right] = pupils.a.x <= pupils.b.x ? [pupils.a, pupils.b] : [pupils.b, pupils.a];
      const t = solveTransform({
        leftPupil: left,
        rightPupil: right,
        assetWidth: overlay.naturalWidth,
        assetHeight: overlay.naturalHeight,
        anchors: selected?.anchors ?? DEFAULT_ANCHORS,
        adjustment: adjust,
      });
      drawFrame(ctx, overlay, t, {
        width: overlay.naturalWidth,
        height: overlay.naturalHeight,
        opacity: selected?.opacity ?? 1,
      });
    }

    // 3. Pupil markers — always shown in manual mode, otherwise on request
    if (pupils && (manual || showGuides)) {
      for (const p of [pupils.a, pupils.b]) {
        ctx.beginPath();
        ctx.arc(p.x, p.y, 11, 0, Math.PI * 2);
        ctx.strokeStyle = "#38bdf8";
        ctx.lineWidth = 3;
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(p.x, p.y, 2.5, 0, Math.PI * 2);
        ctx.fillStyle = "#38bdf8";
        ctx.fill();
      }
      ctx.beginPath();
      ctx.moveTo(pupils.a.x, pupils.a.y);
      ctx.lineTo(pupils.b.x, pupils.b.y);
      ctx.strokeStyle = "rgba(56,189,248,0.45)";
      ctx.lineWidth = 1.5;
      ctx.stroke();
    }
  }, [mode, pupils, manual, showGuides, adjust, selected]);

  // Still-photo mode draws synchronously from an effect rather than through
  // requestAnimationFrame. rAF is suspended in a background tab, which would
  // otherwise leave the canvas blank until the customer touched a control
  // after switching back. The camera path keeps its own rAF loop, where
  // frame-rate pacing is the whole point.

  // --- Overlay image loading ---------------------------------------------
  // The loaded bitmap lives in a ref (it is drawn, never rendered), and a
  // counter tells the render effect below that a new one has arrived.
  useEffect(() => {
    if (!selected?.overlayUrl) {
      overlayRef.current = null;
      return;
    }
    let cancelled = false;

    const img = new Image();
    img.crossOrigin = "anonymous";
    img.onload = () => {
      if (cancelled) return;
      overlayRef.current = img;
      setOverlayLoads((n) => n + 1);
    };
    img.onerror = () => {
      if (cancelled) return;
      overlayRef.current = null;
      setError(`Couldn't load the try-on image for ${selected.name}.`);
    };
    img.src = selected.overlayUrl;

    return () => {
      // A fast click through the frame picker must not let a slow earlier
      // image win the race and draw the wrong frame.
      cancelled = true;
    };
  }, [selected?.overlayUrl, selected?.name]);

  useEffect(() => {
    if (mode === "upload") renderOnce();
  }, [mode, renderOnce, pupils, adjust, selected, manual, showGuides, overlayLoads]);

  // --- Photo upload -------------------------------------------------------
  const onPhotoChosen = async (file: File) => {
    setError(null);
    setSaved(null);
    setBusy("Reading photo…");

    const url = URL.createObjectURL(file);
    const img = new Image();
      img.onload = async () => {
        photoRef.current = img;
        setHasPhoto(true);
        setMode("upload");
        stopCamera();

        const box = fitContain(img.naturalWidth, img.naturalHeight, CANVAS_W, CANVAS_H);
        let found = false;

        if (detectStatus === "ready") {
          setBusy("Finding your eyes…");
          try {
            const lms = await detectImage(img);
            if (lms) {
              const m = measureFace(lms, box.width, box.height);
              if (m) {
                // measureFace works in the photo's own pixel space; shift into
                // canvas space by the letterbox offset.
                const shift = (p: Point) => ({ x: p.x + box.x, y: p.y + box.y });
                setPupils({ a: shift(m.leftPupil), b: shift(m.rightPupil) });
                setMeasurement(m);
                setFaceShape(estimateFaceShape(lms, box.width, box.height));
                setManual(false);
                found = true;
              }
            }
          } catch (err) {
            console.warn("[try-on] detection failed on upload", err);
          }
        }

        if (!found) {
          // Seed the markers roughly where eyes usually are and let the
          // customer drag them the last few pixels.
          setPupils({
            a: { x: box.x + box.width * 0.38, y: box.y + box.height * 0.42 },
            b: { x: box.x + box.width * 0.62, y: box.y + box.height * 0.42 },
          });
          setMeasurement(null);
          setFaceShape(null);
          setManual(true);
        }

        setAdjust(NO_ADJUSTMENT);
        setBusy(null);
        URL.revokeObjectURL(url);
        // The state changes above schedule the render effect; no manual draw
        // is needed here.
      };
    img.onerror = () => {
      setBusy(null);
      setError("That file could not be opened as an image.");
      URL.revokeObjectURL(url);
    };
    img.src = url;
  };

  // --- Camera -------------------------------------------------------------
  const stopCamera = () => {
    cancelAnimationFrame(rafRef.current);
    streamRef.current?.getTracks().forEach((t) => t.stop());
    streamRef.current = null;
  };

  const startCamera = async () => {
    setError(null);
    setSaved(null);
    setBusy("Starting camera…");
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });
      streamRef.current = stream;
      const video = videoRef.current!;
      video.srcObject = stream;
      await video.play();

      photoRef.current = null;
      setHasPhoto(false);
      setMode("camera");
      setAdjust(NO_ADJUSTMENT);
      setBusy(null);

      let lastTs = -1;
      const loop = async () => {
        const v = videoRef.current;
        if (!v || !streamRef.current) return;

        if (detectStatus === "ready" && v.readyState >= 2) {
          const ts = performance.now();
          if (ts > lastTs) {
            lastTs = ts;
            try {
              const lms = await detectVideo(v, ts);
              if (lms) {
                const box = fitCover(v.videoWidth, v.videoHeight, CANVAS_W, CANVAS_H);
                const m = measureFace(lms, box.width, box.height);
                if (m) {
                  // Canvas shows a mirrored image, so mirror the points too.
                  const toCanvas = (p: Point) => ({
                    x: CANVAS_W - (p.x + box.x),
                    y: p.y + box.y,
                  });
                  setPupils({ a: toCanvas(m.leftPupil), b: toCanvas(m.rightPupil) });
                  setMeasurement(m);
                }
              }
            } catch {
              // A dropped frame is not worth surfacing; the next one will do.
            }
          }
        }

        renderOnce();
        rafRef.current = requestAnimationFrame(loop);
      };
      rafRef.current = requestAnimationFrame(loop);
    } catch (err) {
      setBusy(null);
      const name = err instanceof Error ? err.name : "";
      setError(
        name === "NotAllowedError"
          ? "Camera access was blocked. Allow it in your browser's address bar, or upload a photo instead."
          : name === "NotFoundError"
            ? "No camera found on this device. Upload a photo instead."
            : "The camera could not be started. Upload a photo instead.",
      );
    }
  };

  // Release the camera when the studio unmounts. Written inline rather than
  // depending on `stopCamera` so the cleanup can never re-run mid-session and
  // cut the feed while the customer is still using it.
  useEffect(() => {
    return () => {
      cancelAnimationFrame(rafRef.current);
      streamRef.current?.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
    };
  }, []);

  // --- Manual pupil dragging ---------------------------------------------
  const canvasPoint = (e: React.PointerEvent<HTMLCanvasElement>): Point => {
    const rect = e.currentTarget.getBoundingClientRect();
    return {
      x: ((e.clientX - rect.left) / rect.width) * CANVAS_W,
      y: ((e.clientY - rect.top) / rect.height) * CANVAS_H,
    };
  };

  const onPointerDown = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!manual || !pupils) return;
    const p = canvasPoint(e);
    const da = Math.hypot(p.x - pupils.a.x, p.y - pupils.a.y);
    const db = Math.hypot(p.x - pupils.b.x, p.y - pupils.b.y);
    const near = Math.min(da, db);
    if (near > 60) return;
    draggingRef.current = da <= db ? "a" : "b";
    e.currentTarget.setPointerCapture(e.pointerId);
  };

  const onPointerMove = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!draggingRef.current || !pupils) return;
    const p = canvasPoint(e);
    setPupils({ ...pupils, [draggingRef.current]: p } as { a: Point; b: Point });
  };

  const onPointerUp = () => {
    draggingRef.current = null;
  };

  // --- Snapshot -----------------------------------------------------------
  const snapshotBlob = useCallback(async (): Promise<Blob | null> => {
    renderOnce();
    const canvas = canvasRef.current;
    if (!canvas) return null;
    return new Promise((resolve) => canvas.toBlob((b) => resolve(b), "image/jpeg", 0.9));
  }, [renderOnce]);

  const download = useCallback(async () => {
    const blob = await snapshotBlob();
    if (!blob) return;
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `tryon-${selected?.slug ?? "frame"}.jpg`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }, [snapshotBlob, selected]);

  const saveToFile = useCallback(async () => {
    if (!selected) return;
    setBusy("Saving…");
    setError(null);
    try {
      const blob = await snapshotBlob();
      if (!blob) throw new Error("Nothing to save yet.");
      const body = new FormData();
      body.append("image", blob, "snapshot.jpg");
      body.append("variantId", selected.variantId);
      body.append("source", mode);
      if (measurement?.pdMm) body.append("pdMm", String(measurement.pdMm));
      if (measurement?.confidence) body.append("pdConfidence", String(measurement.confidence));
      if (faceShape) body.append("faceShape", faceShape);

      const res = await fetch("/api/tryon/snapshot", { method: "POST", body });
      const json = await res.json();
      if (!res.ok) throw new Error(json.error || "Save failed");
      setSaved("Saved to your file. Our optician can see it when preparing your lenses.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save the snapshot.");
    } finally {
      setBusy(null);
    }
  }, [selected, snapshotBlob, mode, measurement, faceShape]);

  const advice = measurementAdvice(measurement);
  const recommendedBand = suggestSizeBand(measurement?.faceWidthMm ?? null);
  const hasSubject = hasPhoto || mode === "camera";

  return (
    <div className={compact ? "space-y-4" : "grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]"}>
      {/* ---------------- Stage ---------------- */}
      <div className="space-y-3">
        <div className="relative overflow-hidden rounded-2xl border border-slate-800 bg-slate-950 shadow-lg">
          <canvas
            ref={canvasRef}
            width={CANVAS_W}
            height={CANVAS_H}
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerCancel={onPointerUp}
            className={`block w-full ${manual ? "cursor-grab touch-none" : ""}`}
            style={{ aspectRatio: `${CANVAS_W} / ${CANVAS_H}` }}
          />
          <video ref={videoRef} playsInline muted className="hidden" />

          {busy && (
            <div className="absolute inset-0 grid place-items-center bg-slate-950/60 text-sm font-medium text-slate-100 backdrop-blur-sm">
              {busy}
            </div>
          )}

          {manual && hasSubject && (
            <p className="absolute inset-x-0 bottom-0 bg-slate-950/80 px-4 py-2 text-center text-xs text-sky-200">
              Drag the two blue circles onto the centre of each pupil.
            </p>
          )}
        </div>

        {/* Source controls */}
        <div className="flex flex-wrap items-center gap-2">
          <label className="cursor-pointer rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800">
            Upload a photo
            <input
              type="file"
              accept="image/*"
              className="hidden"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) void onPhotoChosen(f);
                e.target.value = "";
              }}
            />
          </label>

          {cameraEnabled &&
            (mode === "camera" ? (
              <button
                type="button"
                onClick={() => {
                  stopCamera();
                  setMode("upload");
                }}
                className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium transition hover:bg-slate-100"
              >
                Stop camera
              </button>
            ) : (
              <button
                type="button"
                onClick={() => void startCamera()}
                className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium transition hover:bg-slate-100"
              >
                Use my camera
              </button>
            ))}

          {hasSubject && (
            <>
              <button
                type="button"
                onClick={() => setManual((v) => !v)}
                className="rounded-lg border border-slate-300 px-4 py-2 text-sm transition hover:bg-slate-100"
              >
                {manual ? "Done adjusting eyes" : "Adjust eye points"}
              </button>
              <button
                type="button"
                onClick={() => setShowGuides((v) => !v)}
                className="rounded-lg border border-slate-300 px-4 py-2 text-sm transition hover:bg-slate-100"
              >
                {showGuides ? "Hide guides" : "Show guides"}
              </button>
            </>
          )}
        </div>

        {error && (
          <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
            {error}
          </p>
        )}
        {saved && (
          <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-800">{saved}</p>
        )}
        {detectStatus === "unavailable" && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
            Automatic eye detection isn&apos;t available in this browser, so you&apos;ll place the
            two eye markers yourself. Everything else works exactly the same.
          </p>
        )}
        {advice && hasSubject && (
          <p className="rounded-lg bg-sky-50 px-3 py-2 text-sm text-sky-800">{advice}</p>
        )}
        <p className="text-xs text-slate-500">
          Your photo and camera feed stay on this device. Nothing is uploaded unless you press
          &ldquo;Save to my file&rdquo;.
        </p>
      </div>

      {/* ---------------- Side panel ---------------- */}
      <div className="space-y-5">
        {selected && (
          <div className="rounded-xl border border-slate-200 p-4">
            <p className="text-xs uppercase tracking-wide text-slate-500">{selected.brand}</p>
            <h3 className="text-lg font-semibold">{selected.name}</h3>
            <p className="text-sm text-slate-600">{selected.colorName}</p>
            <p className="mt-1 text-lg font-semibold">{formatMoney(selected.priceMinor)}</p>
            <Link
              href={`/frames/${selected.slug}?variant=${selected.variantId}`}
              className="mt-3 inline-block rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-800"
            >
              Choose lenses
            </Link>
          </div>
        )}

        {/* Frame picker */}
        <div>
          <h4 className="mb-2 text-sm font-semibold text-slate-700">
            Try another frame ({frames.length})
          </h4>
          <div className="grid max-h-72 grid-cols-3 gap-2 overflow-y-auto pr-1">
            {frames.map((f) => (
              <button
                key={f.variantId}
                type="button"
                onClick={() => {
                  setSelectedId(f.variantId);
                  setAdjust(NO_ADJUSTMENT);
                }}
                title={`${f.name} — ${f.colorName}`}
                className={`rounded-lg border p-1 transition ${
                  f.variantId === selectedId
                    ? "border-slate-900 ring-2 ring-slate-900"
                    : "border-slate-200 hover:border-slate-400"
                }`}
              >
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={f.thumbUrl || f.overlayUrl || ""}
                  alt={`${f.name} in ${f.colorName}`}
                  className="h-12 w-full object-contain"
                  loading="lazy"
                />
              </button>
            ))}
          </div>
        </div>

        {/* Fit sliders */}
        {hasSubject && (
          <div className="space-y-3 rounded-xl border border-slate-200 p-4">
            <h4 className="text-sm font-semibold text-slate-700">Fine-tune the fit</h4>
            <Slider
              label="Size"
              min={0.75}
              max={1.35}
              step={0.01}
              value={adjust.scale}
              onChange={(scale) => setAdjust((a) => ({ ...a, scale }))}
            />
            <Slider
              label="Height on nose"
              min={-60}
              max={60}
              step={1}
              value={adjust.offsetY}
              onChange={(offsetY) => setAdjust((a) => ({ ...a, offsetY }))}
            />
            <Slider
              label="Tilt"
              min={-0.25}
              max={0.25}
              step={0.005}
              value={adjust.rotate}
              onChange={(rotate) => setAdjust((a) => ({ ...a, rotate }))}
            />
            <button
              type="button"
              onClick={() => setAdjust(NO_ADJUSTMENT)}
              className="text-xs font-medium text-slate-500 underline underline-offset-2"
            >
              Reset to automatic fit
            </button>
          </div>
        )}

        {/* Measurements */}
        {measurement?.pdMm && (
          <div className="rounded-xl border border-slate-200 p-4">
            <h4 className="text-sm font-semibold text-slate-700">Your measurements</h4>
            <dl className="mt-2 space-y-1 text-sm">
              <div className="flex justify-between">
                <dt className="text-slate-600">Pupillary distance</dt>
                <dd className="font-medium">{measurement.pdMm.toFixed(1)} mm</dd>
              </div>
              {measurement.faceWidthMm && (
                <div className="flex justify-between">
                  <dt className="text-slate-600">Face width</dt>
                  <dd className="font-medium">{measurement.faceWidthMm.toFixed(0)} mm</dd>
                </div>
              )}
              {faceShape && (
                <div className="flex justify-between">
                  <dt className="text-slate-600">Face shape</dt>
                  <dd className="font-medium capitalize">{faceShape}</dd>
                </div>
              )}
              {recommendedBand && (
                <div className="flex justify-between">
                  <dt className="text-slate-600">Suggested size</dt>
                  <dd className="font-medium capitalize">{recommendedBand}</dd>
                </div>
              )}
            </dl>
            <p className="mt-2 text-xs text-slate-500">
              An estimate from your photo, accurate to about ±2 mm. Our optician confirms it before
              your lenses are cut.
            </p>
          </div>
        )}

        {/* Actions */}
        {hasSubject && (
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void download()}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium transition hover:bg-slate-100"
            >
              Download photo
            </button>
            {canSave && (
              <button
                type="button"
                onClick={() => void saveToFile()}
                className="rounded-lg bg-sky-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-sky-700"
              >
                Save to my file
              </button>
            )}
          </div>
        )}
      </div>
    </div>
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
      <span className="mb-1 block text-xs font-medium text-slate-600">{label}</span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="w-full accent-slate-900"
      />
    </label>
  );
}
