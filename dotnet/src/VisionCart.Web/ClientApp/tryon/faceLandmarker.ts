import type { NormalizedLandmark } from "./geometry.ts";

/**
 * Loads the MediaPipe face landmark model once and hands back a detector.
 *
 * Port of the legacy `useFaceLandmarker` React hook, rewritten as a plain
 * async factory — there is no React here, and the studio needs the detector as
 * an object rather than as hook state.
 *
 * The model and its WebAssembly runtime are served from this application's own
 * wwwroot, never a CDN. That is the whole privacy architecture: no third party
 * ever learns that a particular visitor is using the try-on, and the feature
 * keeps working on a network where Google's CDN is unreachable.
 *
 * If either asset is missing — a deployment that skipped them, an air-gapped
 * machine, a browser without the required APIs — this reports `unavailable`
 * rather than throwing, and the studio falls back to letting the customer place
 * their own pupil markers.
 */

export type LandmarkerStatus = "idle" | "loading" | "ready" | "unavailable";

export interface FaceDetector {
  readonly status: LandmarkerStatus;
  readonly reason: string | null;
  detectImage(image: HTMLImageElement | HTMLCanvasElement): NormalizedLandmark[] | null;
  detectVideo(video: HTMLVideoElement, timestampMs: number): NormalizedLandmark[] | null;
  close(): void;
}

interface MediaPipeModule {
  FilesetResolver: {
    forVisionTasks(wasmPath: string): Promise<unknown>;
  };
  FaceLandmarker: {
    createFromOptions(fileset: unknown, options: unknown): Promise<MediaPipeLandmarker>;
  };
}

interface MediaPipeLandmarker {
  detect(image: HTMLImageElement | HTMLCanvasElement): { faceLandmarks?: NormalizedLandmark[][] };
  detectForVideo(
    video: HTMLVideoElement,
    timestampMs: number,
  ): { faceLandmarks?: NormalizedLandmark[][] };
  setOptions(options: { runningMode: "IMAGE" | "VIDEO" }): Promise<void>;
  close(): void;
}

export interface FaceDetectorOptions {
  modelUrl: string;
  wasmDir: string;
  vendorUrl: string;
}

class Unavailable implements FaceDetector {
  readonly status: LandmarkerStatus = "unavailable";
  constructor(readonly reason: string | null) {}
  detectImage(): null { return null; }
  detectVideo(): null { return null; }
  close(): void { /* nothing to release */ }
}

class MediaPipeDetector implements FaceDetector {
  readonly status: LandmarkerStatus = "ready";
  readonly reason = null;

  private mode: "IMAGE" | "VIDEO" = "IMAGE";
  private closed = false;

  constructor(private readonly landmarker: MediaPipeLandmarker) {}

  detectImage(image: HTMLImageElement | HTMLCanvasElement): NormalizedLandmark[] | null {
    if (this.closed) return null;
    this.ensureMode("IMAGE");
    const result = this.landmarker.detect(image);
    return result.faceLandmarks?.[0] ?? null;
  }

  detectVideo(video: HTMLVideoElement, timestampMs: number): NormalizedLandmark[] | null {
    if (this.closed) return null;
    this.ensureMode("VIDEO");
    const result = this.landmarker.detectForVideo(video, timestampMs);
    return result.faceLandmarks?.[0] ?? null;
  }

  /**
   * MediaPipe refuses a video frame while in IMAGE mode and vice versa. The
   * switch is fire-and-forget: the first call after a mode change may return
   * nothing, and the next frame picks it up.
   */
  private ensureMode(mode: "IMAGE" | "VIDEO"): void {
    if (this.mode === mode) return;
    this.mode = mode;
    void this.landmarker.setOptions({ runningMode: mode });
  }

  close(): void {
    if (this.closed) return;
    this.closed = true;
    this.landmarker.close();
  }
}

export async function createFaceDetector(options: FaceDetectorOptions): Promise<FaceDetector> {
  try {
    // The model file is ~3.7 MB. Check it is actually there before paying for
    // the WebAssembly download, so the manual fallback kicks in fast.
    const head = await fetch(options.modelUrl, { method: "HEAD" });
    if (!head.ok) throw new Error(`model not found at ${options.modelUrl}`);

    const vision = (await import(/* @vite-ignore */ options.vendorUrl)) as MediaPipeModule;
    const fileset = await vision.FilesetResolver.forVisionTasks(options.wasmDir);

    const landmarker = await vision.FaceLandmarker.createFromOptions(fileset, {
      baseOptions: { modelAssetPath: options.modelUrl, delegate: "GPU" },
      runningMode: "IMAGE",
      numFaces: 1,
      outputFaceBlendshapes: false,
      outputFacialTransformationMatrixes: false,
    });

    return new MediaPipeDetector(landmarker);
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.warn("[try-on] automatic face detection unavailable:", reason);
    return new Unavailable(reason);
  }
}
