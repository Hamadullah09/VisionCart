"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { FaceLandmarker } from "@mediapipe/tasks-vision";
import type { NormalizedLandmark } from "@/lib/tryon";

/**
 * Loads the MediaPipe face landmark model once and hands back a detector.
 *
 * The model and wasm runtime are served from /public (see
 * `npm run tryon:assets`). If either is missing — a fresh clone that never ran
 * the fetch script, an air-gapped machine, a browser without the required
 * APIs — this reports `unavailable` rather than throwing, and the try-on falls
 * back to letting the customer place their own pupil markers.
 */

export type LandmarkerStatus = "idle" | "loading" | "ready" | "unavailable";

const MODEL_URL = process.env.NEXT_PUBLIC_TRYON_MODEL_URL || "/models/face_landmarker.task";
const WASM_DIR = process.env.NEXT_PUBLIC_TRYON_WASM_DIR || "/wasm";

export function useFaceLandmarker(enabled = true) {
  const [status, setStatus] = useState<LandmarkerStatus>("idle");
  const [reason, setReason] = useState<string | null>(null);
  const landmarkerRef = useRef<FaceLandmarker | null>(null);
  const modeRef = useRef<"IMAGE" | "VIDEO">("IMAGE");

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;

    (async () => {
      setStatus("loading");
      try {
        // The model file is ~3.7 MB; check it is actually there before paying
        // for the wasm download, so the fallback kicks in fast.
        const head = await fetch(MODEL_URL, { method: "HEAD" });
        if (!head.ok) throw new Error(`model not found at ${MODEL_URL}`);

        const vision = await import("@mediapipe/tasks-vision");
        const fileset = await vision.FilesetResolver.forVisionTasks(WASM_DIR);
        const landmarker = await vision.FaceLandmarker.createFromOptions(fileset, {
          baseOptions: { modelAssetPath: MODEL_URL, delegate: "GPU" },
          runningMode: "IMAGE",
          numFaces: 1,
          outputFaceBlendshapes: false,
          outputFacialTransformationMatrixes: false,
        });

        if (cancelled) {
          landmarker.close();
          return;
        }
        landmarkerRef.current = landmarker;
        setStatus("ready");
      } catch (err) {
        if (cancelled) return;
        console.warn("[try-on] automatic face detection unavailable:", err);
        setReason(err instanceof Error ? err.message : String(err));
        setStatus("unavailable");
      }
    })();

    return () => {
      cancelled = true;
      landmarkerRef.current?.close();
      landmarkerRef.current = null;
    };
  }, [enabled]);

  /** Detect on a still image or canvas. */
  const detectImage = useCallback(
    async (source: HTMLImageElement | HTMLCanvasElement): Promise<NormalizedLandmark[] | null> => {
      const lm = landmarkerRef.current;
      if (!lm) return null;
      if (modeRef.current !== "IMAGE") {
        await lm.setOptions({ runningMode: "IMAGE" });
        modeRef.current = "IMAGE";
      }
      const result = lm.detect(source);
      return result.faceLandmarks?.[0] ?? null;
    },
    [],
  );

  /** Detect on a video frame. `timestamp` must increase monotonically. */
  const detectVideo = useCallback(
    async (video: HTMLVideoElement, timestamp: number): Promise<NormalizedLandmark[] | null> => {
      const lm = landmarkerRef.current;
      if (!lm) return null;
      if (modeRef.current !== "VIDEO") {
        await lm.setOptions({ runningMode: "VIDEO" });
        modeRef.current = "VIDEO";
      }
      const result = lm.detectForVideo(video, timestamp);
      return result.faceLandmarks?.[0] ?? null;
    },
    [],
  );

  return { status, reason, detectImage, detectVideo };
}
