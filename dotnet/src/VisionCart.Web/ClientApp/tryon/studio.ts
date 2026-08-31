import {
  DEFAULT_ANCHORS,
  NO_ADJUSTMENT,
  distance,
  drawFrame,
  estimateFaceShape,
  fitContain,
  fitCover,
  measureFace,
  measurementAdvice,
  autoFit,
  type AutoFit,
  solveTransform,
  suggestSizeBand,
  type Adjustment,
  type FaceMeasurement,
  type FrameAnchors,
  type Point,
} from "./geometry.ts";
import { createFaceDetector, type FaceDetector } from "./faceLandmarker.ts";
import {
  estimatePose,
  yawForeshortening,
  yawBridgeShift,
  NEUTRAL_POSE,
  type HeadPose,
} from "./pose.ts";
import { PoseSmoother, holdThroughLoss } from "./smoothing.ts";

/**
 * The virtual mirror.
 *
 * Port of the legacy `TryOnStudio` React component, rewritten as a plain class
 * that drives DOM the Razor view rendered. The placement mathematics is
 * untouched: it lives in `geometry.ts`, which is byte-identical to the legacy
 * `src/lib/tryon.ts` and covered by 29 tests.
 *
 * Two ways in: upload a photo, or open the camera. Both end up in the same
 * place — two pupil points on a canvas — after which the frame overlay is placed
 * by pure geometry. Face detection is a convenience; if it is unavailable or the
 * photo defeats it, the customer drags two markers onto their eyes and
 * everything downstream works identically.
 *
 * Nothing is uploaded. The photo and every rendered frame stay inside the
 * browser until the customer presses "Save to my file".
 */

/**
 * The long edge of the drawing surface, in logical units.
 *
 * The stage takes the shape of whatever it is showing, so the *aspect* varies;
 * this keeps the amount of detail constant across shapes. A portrait photo used
 * to be letterboxed into a fixed 4:3 landscape box and ended up occupying barely
 * half the width — reduced to about 600 real pixels from a 3000-pixel original,
 * which is what made an uploaded photo look soft and washed out.
 */
const LOGICAL_LONG_EDGE = 900;

/** Shown before any media arrives, and the shape the camera usually takes. */
const DEFAULT_ASPECT = 4 / 3;

/**
 * How far the stage will stretch to match its media. Beyond these the page
 * layout suffers more than the picture gains — an 18:9 phone panorama would
 * otherwise squeeze the frame picker off the screen.
 */
const MIN_ASPECT = 0.62;
const MAX_ASPECT = 1.90;

/**
 * Ceiling on backing-store size, in multiples of the logical canvas. 2.5 covers
 * a full-width stage on a 2x display; beyond that the memory and per-frame cost
 * buy detail the eye cannot resolve.
 */
const MAX_BACKING_SCALE = 2.5;

/** Most of the window a tall portrait may claim, leaving room for the controls. */
const MAX_STAGE_HEIGHT_FRACTION = 0.72;

/** How the width verdict is worded for a customer. */
const VERDICT_TEXT: Record<string, string> = {
  good: "Good fit",
  narrow: "Too narrow",
  wide: "Too wide",
  unknown: "Not measured",
};

/** How close a pointer must land to a marker before it grabs it. */
const GRAB_RADIUS = 60;

export interface TryOnFrameData {
  variantId: string;
  frameId: string;
  slug: string;
  name: string;
  brand: string | null;
  colorName: string;
  colorHex: string | null;
  overlayUrl: string | null;
  thumbUrl: string | null;
  priceText: string;
  anchors: FrameAnchors;
  opacity: number;
}

export interface StudioConfig {
  frames: TryOnFrameData[];
  initialVariantId?: string;
  /** Saving needs both a signed-in customer and the store setting enabled. */
  canSave: boolean;
  cameraEnabled: boolean;
  modelUrl: string;
  wasmDir: string;
  vendorUrl: string;
  snapshotUrl: string;
  antiforgeryToken: string;
}

type Mode = "upload" | "camera";

export class TryOnStudio {
  private readonly canvas: HTMLCanvasElement;
  private readonly ctx: CanvasRenderingContext2D;

  /** Real pixels per logical unit. */
  private backingScale = 1;

  /** The size, height and tilt worked out for the current face and frame. */
  private fit: AutoFit | null = null;

  /** Where the head is pointing, and how much that reading is trusted. */
  private pose: HeadPose = NEUTRAL_POSE;

  /** Damps landmark noise without lagging behind real movement. */
  private readonly smoother = new PoseSmoother();

  /** When the face was last actually found, for riding out brief losses. */
  private lastDetectionMs = 0;

  /** Multiplies the frame's opacity while a lost face fades out. */
  private trackingOpacity = 1;

  /** Latched, so the hint does not flicker on and off around the threshold. */
  private poseHintShown = false;

  /** The logical drawing surface. Reshaped to match the photo or camera feed. */
  private logicalW = LOGICAL_LONG_EDGE;
  private logicalH = Math.round(LOGICAL_LONG_EDGE / DEFAULT_ASPECT);
  private readonly video: HTMLVideoElement;

  private detector: FaceDetector | null = null;

  private mode: Mode = "upload";
  private selectedId: string;
  /**
   * The fit, as computed. Not a user preference: size, height and tilt are
   * measured from the face, and a slider over them only lets a customer drag
   * away from a correct fit and then judge the frame on a wrong rendering.
   */
  private adjust: Adjustment = { ...NO_ADJUSTMENT };
  private manual = false;
  private showGuides = false;
  private pupils: { a: Point; b: Point } | null = null;
  private measurement: FaceMeasurement | null = null;
  private faceShape: string | null = null;
  private hasPhoto = false;

  private photo: HTMLImageElement | null = null;
  private overlay: HTMLImageElement | null = null;
  private stream: MediaStream | null = null;
  private rafHandle = 0;
  private dragging: "a" | "b" | null = null;
  private overlayToken = 0;

  constructor(private readonly root: HTMLElement, private readonly config: StudioConfig) {
    this.canvas = this.require<HTMLCanvasElement>("[data-tryon-canvas]");
    this.video = this.require<HTMLVideoElement>("[data-tryon-video]");

    const ctx = this.canvas.getContext("2d");
    if (!ctx) throw new Error("This browser cannot draw to a canvas.");
    this.ctx = ctx;

    this.resizeBacking();

    // The stage is fluid, so the displayed size changes with the window and
    // the backing store has to follow it.
    if (typeof ResizeObserver !== "undefined") {
      const observer = new ResizeObserver(() => {
        if (this.resizeBacking()) this.render();
      });
      // The stage, not the canvas — observing the element we resize would loop.
      observer.observe(this.canvas.parentElement ?? this.canvas);
    }

    this.selectedId = config.initialVariantId ?? config.frames[0]?.variantId ?? "";

    this.bindControls();
    this.renderFramePicker();
    void this.loadOverlay();
    this.render();
    void this.initialiseDetector();

    // Release the camera if the page goes away mid-session.
    window.addEventListener("pagehide", () => this.stopCamera());
  }

  // --- wiring -------------------------------------------------------------

  private require<T extends Element>(selector: string): T {
    const el = this.root.querySelector<T>(selector);
    if (!el) throw new Error(`Try-on markup is missing ${selector}`);
    return el;
  }

  private find<T extends Element>(selector: string): T | null {
    return this.root.querySelector<T>(selector);
  }

  private get selected(): TryOnFrameData | null {
    return this.config.frames.find(f => f.variantId === this.selectedId)
      ?? this.config.frames[0]
      ?? null;
  }

  private bindControls(): void {
    this.find<HTMLInputElement>("[data-tryon-file]")?.addEventListener("change", event => {
      const input = event.currentTarget as HTMLInputElement;
      const file = input.files?.[0];
      if (file) void this.onPhotoChosen(file);
      input.value = "";
    });

    this.find<HTMLButtonElement>("[data-tryon-camera]")?.addEventListener("click", () => {
      if (this.mode === "camera") {
        this.stopCamera();
        this.mode = "upload";
        this.syncChrome();
        this.render();
      } else {
        void this.startCamera();
      }
    });

    this.find<HTMLButtonElement>("[data-tryon-manual]")?.addEventListener("click", () => {
      this.manual = !this.manual;
      this.syncChrome();
      this.render();
    });

    this.find<HTMLButtonElement>("[data-tryon-guides]")?.addEventListener("click", () => {
      this.showGuides = !this.showGuides;
      this.syncChrome();
      this.render();
    });

    this.find<HTMLButtonElement>("[data-tryon-download]")?.addEventListener("click", () => {
      void this.download();
    });

    this.find<HTMLButtonElement>("[data-tryon-save]")?.addEventListener("click", () => {
      void this.saveToFile();
    });

    // Manual pupil dragging.
    this.canvas.addEventListener("pointerdown", e => this.onPointerDown(e));
    this.canvas.addEventListener("pointermove", e => this.onPointerMove(e));
    this.canvas.addEventListener("pointerup", () => { this.dragging = null; });
    this.canvas.addEventListener("pointercancel", () => { this.dragging = null; });
  }

  private renderFramePicker(): void {
    const list = this.find<HTMLElement>("[data-tryon-frames]");
    if (!list) return;

    list.textContent = "";

    for (const frame of this.config.frames) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "tryon-thumb";
      button.dataset.variantId = frame.variantId;
      button.title = `${frame.name} — ${frame.colorName}`;
      button.setAttribute("aria-label", button.title);

      if (frame.thumbUrl ?? frame.overlayUrl) {
        const img = document.createElement("img");
        img.src = (frame.thumbUrl ?? frame.overlayUrl)!;
        img.alt = "";
        img.loading = "lazy";
        button.append(img);
      }

      button.addEventListener("click", () => {
        this.selectedId = frame.variantId;

        // The fit is a property of this face AND this frame, so a new frame
        // needs a new answer — the same face wants a different size in a
        // 145 mm frame than in a 125 mm one.
        this.applyAutoFit();
        this.syncMeasurements();
        this.syncChrome();
        void this.loadOverlay();
      });

      list.append(button);
    }

    this.syncChrome();
  }

  private async initialiseDetector(): Promise<void> {
    this.setBusy("Preparing the mirror…");
    this.detector = await createFaceDetector({
      modelUrl: this.config.modelUrl,
      wasmDir: this.config.wasmDir,
      vendorUrl: this.config.vendorUrl,
    });
    this.setBusy(null);

    if (this.detector.status === "unavailable") {
      this.setNotice(
        "fallback",
        "Automatic eye detection isn't available in this browser, so you'll place the two " +
        "eye markers yourself. Everything else works exactly the same.",
      );
    }
  }

  // --- rendering ----------------------------------------------------------

  /**
   * Draws one frame. Still-photo mode calls this synchronously whenever
   * something changes, rather than through requestAnimationFrame.
   *
   * That is deliberate and must not be "tidied up" into a rAF loop: rAF is
   * suspended in a background tab, which would leave the canvas blank until the
   * customer touched a control after switching back. The camera path below keeps
   * its own rAF loop, where frame-rate pacing is the whole point.
   */
  private render(): void {
    const ctx = this.ctx;

    // Everything below is written in the 900x675 logical space the geometry
    // uses. This maps that onto however many real pixels the backing store has.
    ctx.setTransform(this.backingScale, 0, 0, this.backingScale, 0, 0);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";

    ctx.clearRect(0, 0, this.logicalW, this.logicalH);
    ctx.fillStyle = "#0b0f14";
    ctx.fillRect(0, 0, this.logicalW, this.logicalH);

    // 1. The person
    let drew = false;

    if (this.mode === "camera" && this.video.readyState >= 2) {
      const box = fitCover(this.video.videoWidth, this.video.videoHeight, this.logicalW, this.logicalH);
      ctx.save();
      // Mirror, so it behaves like a real mirror rather than a video call.
      ctx.translate(this.logicalW, 0);
      ctx.scale(-1, 1);
      ctx.drawImage(this.video, this.logicalW - box.x - box.width, box.y, box.width, box.height);
      ctx.restore();
      drew = true;
    } else if (this.photo) {
      const box = fitContain(this.photo.naturalWidth, this.photo.naturalHeight, this.logicalW, this.logicalH);
      ctx.drawImage(this.photo, box.x, box.y, box.width, box.height);
      drew = true;
    }

    if (!drew) {
      ctx.fillStyle = "#5b6b7d";
      ctx.font = "500 22px system-ui, sans-serif";
      ctx.textAlign = "center";
      ctx.fillText("Upload a photo or start the camera", this.logicalW / 2, this.logicalH / 2);
      return;
    }

    // 2. The frame
    if (this.overlay && this.pupils) {
      // Order by x so the artwork never lands mirrored, whatever the source.
      const [left, right] = this.pupils.a.x <= this.pupils.b.x
        ? [this.pupils.a, this.pupils.b]
        : [this.pupils.b, this.pupils.a];

      // Turning the head foreshortens the frame horizontally and slides the
      // bridge across the face, because the bridge rests on the nose and the
      // nose is in front of the eyes. Without both, a turned head wears a
      // frame that is visibly too wide and detached from the nose.
      const pupilSpan = distance(left, right);
      const shiftX = yawBridgeShift(this.pose.yawDeg) * pupilSpan;

      const transform = solveTransform({
        leftPupil: left,
        rightPupil: right,
        assetWidth: this.overlay.naturalWidth,
        assetHeight: this.overlay.naturalHeight,
        anchors: this.selected?.anchors ?? DEFAULT_ANCHORS,
        adjustment: {
          ...this.adjust,
          offsetX: this.adjust.offsetX + shiftX,
        },
      });

      drawFrame(ctx, this.overlay, transform, {
        width: this.overlay.naturalWidth,
        height: this.overlay.naturalHeight,
        opacity: (this.selected?.opacity ?? 1) * this.trackingOpacity,
        squeezeX: yawForeshortening(this.pose.yawDeg),
      });
    }

    // 3. Pupil markers — always in manual mode, otherwise on request
    if (this.pupils && (this.manual || this.showGuides)) {
      for (const point of [this.pupils.a, this.pupils.b]) {
        ctx.beginPath();
        ctx.arc(point.x, point.y, 11, 0, Math.PI * 2);
        ctx.strokeStyle = "#38bdf8";
        ctx.lineWidth = 3;
        ctx.stroke();

        ctx.beginPath();
        ctx.arc(point.x, point.y, 2.5, 0, Math.PI * 2);
        ctx.fillStyle = "#38bdf8";
        ctx.fill();
      }

      ctx.beginPath();
      ctx.moveTo(this.pupils.a.x, this.pupils.a.y);
      ctx.lineTo(this.pupils.b.x, this.pupils.b.y);
      ctx.strokeStyle = "rgba(56,189,248,0.45)";
      ctx.lineWidth = 1.5;
      ctx.stroke();
    }
  }

  /**
   * Loads the overlay artwork for the selected colourway.
   *
   * A token guards the race: clicking quickly through the picker must not let a
   * slow earlier image win and draw the wrong frame.
   */
  private async loadOverlay(): Promise<void> {
    const frame = this.selected;
    const token = ++this.overlayToken;

    if (!frame?.overlayUrl) {
      this.overlay = null;
      this.render();
      return;
    }

    try {
      const image = await loadImage(frame.overlayUrl);
      if (token !== this.overlayToken) return;
      this.overlay = image;
      this.setNotice("error", null);
    } catch {
      if (token !== this.overlayToken) return;
      this.overlay = null;
      this.setNotice("error", `Couldn't load the try-on image for ${frame.name}.`);
    }

    this.render();
  }

  // --- photo upload -------------------------------------------------------

  private async onPhotoChosen(file: File): Promise<void> {
    this.setNotice("error", null);
    this.setNotice("saved", null);
    this.setBusy("Reading photo…");

    const url = URL.createObjectURL(file);

    try {
      const image = await loadImage(url);

      this.photo = image;
      this.hasPhoto = true;
      this.mode = "upload";
      this.stopCamera();

      this.adoptAspect(image.naturalWidth, image.naturalHeight);
      const box = fitContain(image.naturalWidth, image.naturalHeight, this.logicalW, this.logicalH);
      let found = false;

      if (this.detector?.status === "ready") {
        this.setBusy("Finding your eyes…");
        try {
          const landmarks = this.detector.detectImage(image);
          if (landmarks) {
            const measured = measureFace(landmarks, box.width, box.height);
            if (measured) {
              // measureFace works in the photo's own pixel space; shift into
              // canvas space by the letterbox offset.
              const shift = (p: Point): Point => ({ x: p.x + box.x, y: p.y + box.y });
              this.pupils = { a: shift(measured.leftPupil), b: shift(measured.rightPupil) };
              this.measurement = measured;
              this.faceShape = estimateFaceShape(landmarks, box.width, box.height);
              this.manual = false;
              found = true;
            }
          }
        } catch (error) {
          console.warn("[try-on] detection failed on upload", error);
        }
      }

      if (!found) {
        // Seed the markers roughly where eyes usually are and let the customer
        // drag them the last few pixels.
        this.pupils = {
          a: { x: box.x + box.width * 0.38, y: box.y + box.height * 0.42 },
          b: { x: box.x + box.width * 0.62, y: box.y + box.height * 0.42 },
        };
        this.measurement = null;
        this.faceShape = null;
        this.manual = true;
      }

      this.applyAutoFit();
      this.syncChrome();
      this.render();
    } catch {
      this.setNotice("error", "That file could not be opened as an image.");
    } finally {
      this.setBusy(null);
      URL.revokeObjectURL(url);
    }
  }

  // --- camera -------------------------------------------------------------

  private stopCamera(): void {
    cancelAnimationFrame(this.rafHandle);
    this.rafHandle = 0;
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = null;
  }

  private async startCamera(): Promise<void> {
    this.setNotice("error", null);
    this.setNotice("saved", null);
    this.setBusy("Starting camera…");

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });

      this.stream = stream;
      this.video.srcObject = stream;
      await this.video.play();

      this.photo = null;
      this.hasPhoto = false;
      this.mode = "camera";
      this.smoother.reset();
      this.lastDetectionMs = 0;
      this.trackingOpacity = 1;

      // A webcam is usually 16:9 while the default stage is 4:3, so without
      // this the feed is cropped on both sides before anyone sees it.
      this.adoptAspect(this.video.videoWidth, this.video.videoHeight);

      this.adjust = { ...NO_ADJUSTMENT };
      this.syncChrome();
      this.setBusy(null);

      let lastTimestamp = -1;

      const loop = (): void => {
        if (!this.stream) return;

        if (this.detector?.status === "ready" && this.video.readyState >= 2) {
          const timestamp = performance.now();
          if (timestamp > lastTimestamp) {
            lastTimestamp = timestamp;
            try {
              const landmarks = this.detector.detectVideo(this.video, timestamp);
              if (landmarks) {
                const box = fitCover(
                  this.video.videoWidth, this.video.videoHeight, this.logicalW, this.logicalH);
                const measured = measureFace(landmarks, box.width, box.height);
                if (measured) {
                  // The canvas shows a mirrored image, so mirror the points too.
                  const toCanvas = (p: Point): Point => ({
                    x: this.logicalW - (p.x + box.x),
                    y: p.y + box.y,
                  });

                  const raw = estimatePose(landmarks);
                  const a = toCanvas(measured.leftPupil);
                  const b = toCanvas(measured.rightPupil);

                  // Mirrored preview, so a turn to their right reads as a turn
                  // to the left on screen.
                  const smoothed = this.smoother.smooth({
                    leftX: a.x, leftY: a.y,
                    rightX: b.x, rightY: b.y,
                    yawDeg: -raw.yawDeg,
                    pitchDeg: raw.pitchDeg,
                  }, timestamp / 1000);

                  this.pupils = {
                    a: { x: smoothed.leftX, y: smoothed.leftY },
                    b: { x: smoothed.rightX, y: smoothed.rightY },
                  };
                  this.pose = { ...raw, yawDeg: smoothed.yawDeg, pitchDeg: smoothed.pitchDeg };
                  this.measurement = measured;
                  this.lastDetectionMs = timestamp;
                  this.trackingOpacity = 1;
                  this.syncMeasurements();
                  this.syncPoseHint();
                }
              }
            } catch {
              // A dropped frame is not worth surfacing; the next one will do.
            }
          }
        }

        if (this.lastDetectionMs > 0) {
          // A blink, a hand, a turn past the model's limit — detection drops
          // constantly. Hiding on the first miss makes the frame strobe.
          const held = holdThroughLoss(performance.now() - this.lastDetectionMs);
          this.trackingOpacity = held.opacity;
          if (!held.usePrevious) this.pupils = null;
        }

        this.render();
        this.rafHandle = requestAnimationFrame(loop);
      };

      this.rafHandle = requestAnimationFrame(loop);
    } catch (error) {
      this.setBusy(null);
      const name = error instanceof Error ? error.name : "";
      this.setNotice("error",
        name === "NotAllowedError"
          ? "Camera access was blocked. Allow it in your browser's address bar, or upload a photo instead."
          : name === "NotFoundError"
            ? "No camera found on this device. Upload a photo instead."
            : "The camera could not be started. Upload a photo instead.");
    }
  }

  /**
   * Reshapes the stage to match what it is about to show.
   *
   * A portrait photo in a landscape box is mostly black bars, and the picture
   * itself lands on barely half the available pixels. Taking the media's own
   * shape means the photo fills the frame, so every pixel of the stage is
   * carrying picture.
   *
   * The geometry is unaffected: pupils, anchors and the frame overlay are all
   * solved in these same logical units, so they scale with the surface rather
   * than against it.
   */
  private adoptAspect(mediaWidth: number, mediaHeight: number): void {
    if (!mediaWidth || !mediaHeight) return;

    const aspect = Math.min(MAX_ASPECT, Math.max(MIN_ASPECT, mediaWidth / mediaHeight));

    const width = aspect >= 1 ? LOGICAL_LONG_EDGE : Math.round(LOGICAL_LONG_EDGE * aspect);
    const height = aspect >= 1 ? Math.round(LOGICAL_LONG_EDGE / aspect) : LOGICAL_LONG_EDGE;

    if (width === this.logicalW && height === this.logicalH) return;

    this.logicalW = width;
    this.logicalH = height;

    this.resizeBacking();
  }

  /**
   * Sizes the canvas to the pixels it is actually displayed on.
   *
   * The backing store used to be pinned at 900x675 while the CSS stretched it
   * to the width of the stage. On any high-density screen that meant a photo
   * was squeezed into 900 pixels and then blown back up to 1300-odd to be
   * shown — soft on the way in, softer on the way out.
   *
   * Drawing stays in the 900x675 logical space; only the number of real pixels
   * underneath it changes, so the geometry and the anchor contract are
   * untouched. Returns true when the size actually changed.
   */
  private resizeBacking(): boolean {
    const stage = this.canvas.parentElement;
    const available = (stage?.clientWidth || this.canvas.clientWidth || this.logicalW);
    const ceiling = window.innerHeight * MAX_STAGE_HEIGHT_FRACTION;

    // Fit the logical shape inside the space available, honouring both limits.
    // Doing this in script rather than in CSS is deliberate: `width: 100%` with
    // a `max-height` does not narrow the element, it squashes what is drawn in
    // it — which stretched every face sideways on a tall portrait photo.
    const aspect = this.logicalW / this.logicalH;
    let cssWidth = available;
    let cssHeight = cssWidth / aspect;

    if (cssHeight > ceiling) {
      cssHeight = ceiling;
      cssWidth = cssHeight * aspect;
    }

    this.canvas.style.width = `${Math.round(cssWidth)}px`;
    this.canvas.style.height = `${Math.round(cssHeight)}px`;

    const density = window.devicePixelRatio || 1;

    // Capped: a 4K monitor would otherwise ask for a backing store big enough
    // to stall a phone, for detail nobody can see.
    const scale = Math.min((cssWidth * density) / this.logicalW, MAX_BACKING_SCALE);
    const width = Math.round(this.logicalW * scale);
    const height = Math.round(this.logicalH * scale);

    if (this.canvas.width === width && this.canvas.height === height) return false;

    this.canvas.width = width;
    this.canvas.height = height;
    this.backingScale = scale;

    return true;
  }

  // --- manual pupil dragging ----------------------------------------------

  private canvasPoint(event: PointerEvent): Point {
    const rect = this.canvas.getBoundingClientRect();
    return {
      x: ((event.clientX - rect.left) / rect.width) * this.logicalW,
      y: ((event.clientY - rect.top) / rect.height) * this.logicalH,
    };
  }

  private onPointerDown(event: PointerEvent): void {
    if (!this.manual || !this.pupils) return;

    const point = this.canvasPoint(event);
    const toA = Math.hypot(point.x - this.pupils.a.x, point.y - this.pupils.a.y);
    const toB = Math.hypot(point.x - this.pupils.b.x, point.y - this.pupils.b.y);

    if (Math.min(toA, toB) > GRAB_RADIUS) return;

    this.dragging = toA <= toB ? "a" : "b";
    this.canvas.setPointerCapture(event.pointerId);
    event.preventDefault();
  }

  private onPointerMove(event: PointerEvent): void {
    if (!this.dragging || !this.pupils) return;
    this.pupils = { ...this.pupils, [this.dragging]: this.canvasPoint(event) };

    // A hand-placed marker invalidates the machine measurement: the PD was
    // derived from iris width, and the customer has just moved the pupils.
    this.measurement = null;
    this.syncMeasurements();
    this.render();
  }

  // --- snapshot -----------------------------------------------------------

  private snapshotBlob(): Promise<Blob | null> {
    this.render();
    return new Promise(resolve => this.canvas.toBlob(blob => resolve(blob), "image/jpeg", 0.9));
  }

  private async download(): Promise<void> {
    const blob = await this.snapshotBlob();
    if (!blob) return;

    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `tryon-${this.selected?.slug ?? "frame"}.jpg`;
    document.body.append(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }

  /**
   * The only path by which any image from the try-on reaches the server, and it
   * runs only when the customer presses the button. The raw photo and the camera
   * feed are never sent — just the composited result they chose to keep.
   */
  private async saveToFile(): Promise<void> {
    const frame = this.selected;
    if (!frame) return;

    this.setBusy("Saving…");
    this.setNotice("error", null);

    try {
      const blob = await this.snapshotBlob();
      if (!blob) throw new Error("Nothing to save yet.");

      const body = new FormData();
      body.append("image", blob, "snapshot.jpg");
      body.append("variantId", frame.variantId);
      body.append("source", this.mode);
      body.append("__RequestVerificationToken", this.config.antiforgeryToken);

      if (this.measurement?.pdMm) body.append("pdMm", String(this.measurement.pdMm));
      if (this.measurement?.confidence) {
        body.append("pdConfidence", String(this.measurement.confidence));
      }
      if (this.faceShape) body.append("faceShape", this.faceShape);

      const response = await fetch(this.config.snapshotUrl, { method: "POST", body });
      const payload = await response.json().catch(() => ({}));

      if (!response.ok) throw new Error(payload.error ?? "Save failed");

      this.setNotice("saved",
        "Saved to your file. Our optician can see it when preparing your lenses.");
    } catch (error) {
      this.setNotice("error",
        error instanceof Error ? error.message : "Could not save the snapshot.");
    } finally {
      this.setBusy(null);
    }
  }

  // --- chrome -------------------------------------------------------------

  private setBusy(message: string | null): void {
    const el = this.find<HTMLElement>("[data-tryon-busy]");
    if (!el) return;
    el.textContent = message ?? "";
    el.hidden = message === null;
  }

  private setNotice(kind: "error" | "saved" | "fallback" | "advice", message: string | null): void {
    const el = this.find<HTMLElement>(`[data-tryon-notice="${kind}"]`);
    if (!el) return;
    el.textContent = message ?? "";
    el.hidden = !message;
  }

  /**
   * Works out the size, height and tilt this face needs and applies them.
   *
   * Called whenever the subject or the frame changes, because the answer
   * depends on both: the same face needs a different size in a 145 mm frame
   * than in a 125 mm one.
   */
  private applyAutoFit(): void {
    const frame = this.selected;

    this.fit = autoFit(
      this.measurement,
      frame
        ? {
            lensWidthMm: frame.lensWidthMm,
            bridgeWidthMm: frame.bridgeWidthMm,
            templeLengthMm: frame.templeLengthMm,
            totalWidthMm: frame.totalWidthMm,
          }
        : null,
      frame?.anchors ?? DEFAULT_ANCHORS,
    );

    this.adjust = { ...this.fit.adjustment };
  }

  /**
   * Says so when the head has turned past the point a flat overlay can carry.
   *
   * Beyond roughly this angle the far lens would need to be genuinely occluded
   * by the cheek, which a sprite cannot do. Admitting that and asking for a
   * small movement is more use to a customer than silently drawing something
   * they can see is wrong.
   */
  private syncPoseHint(): void {
    if (!this.pose.isExtreme) {
      if (this.poseHintShown) {
        this.setNotice("fallback", null);
        this.poseHintShown = false;
      }
      return;
    }

    if (this.poseHintShown) return;

    this.setNotice("fallback", Math.abs(this.pose.yawDeg) > Math.abs(this.pose.pitchDeg)
      ? "Turn a little towards the camera for a truer fit."
      : "Level your chin towards the camera for a truer fit.");
    this.poseHintShown = true;
  }

  private syncMeasurements(): void {
    const panel = this.find<HTMLElement>("[data-tryon-measurements]");
    if (!panel) return;

    const pd = this.measurement?.pdMm ?? null;

    // Shown whenever there is a subject, not only when a face was found. A
    // customer whose photo could not be measured needs to be told that, and
    // why — an empty panel that simply vanishes reads as the feature being
    // broken rather than the photo being unsuitable.
    panel.hidden = !this.hasSubject;
    if (panel.hidden) return;

    const set = (name: string, value: string | null) => {
      const el = this.find<HTMLElement>(`[data-tryon-measure="${name}"]`);
      const row = el?.closest<HTMLElement>("[data-tryon-measure-row]");
      if (!el) return;
      if (value === null) { if (row) row.hidden = true; return; }
      if (row) row.hidden = false;
      el.textContent = value;
    };

    set("pd", pd === null ? null : `${pd.toFixed(1)} mm`);
    set("faceWidth", this.measurement?.faceWidthMm ? `${this.measurement.faceWidthMm.toFixed(0)} mm` : null);
    set("faceShape", this.faceShape);
    set("sizeBand", suggestSizeBand(this.measurement?.faceWidthMm ?? null));

    const fit = this.fit;
    set("frameWidth", fit?.frameWidthMm ? `${fit.frameWidthMm.toFixed(0)} mm` : null);
    set("fitVerdict", fit && fit.widthRatio
      ? `${VERDICT_TEXT[fit.verdict]} · ${Math.round(fit.widthRatio * 100)}% of face width`
      : null);
    set("fitHeight", fit && fit.heightMm
      ? `${fit.heightMm > 0 ? "+" : ""}${fit.heightMm.toFixed(1)} mm on the nose`
      : null);
    // Only when a face was actually measured — "0.0 degrees, corrected" against
    // a photo nothing was found in claims a reading that was never taken.
    set("fitTilt", fit && this.measurement
      ? `${fit.tiltDeg.toFixed(1)}° head tilt, corrected`
      : null);

    const verdictEl = this.find<HTMLElement>("[data-tryon-fit-verdict]");
    if (verdictEl) {
      verdictEl.className = `tryon-verdict is-${fit?.verdict ?? "unknown"}`;
      verdictEl.hidden = !fit || fit.verdict === "unknown";
    }

    const notesEl = this.find<HTMLElement>("[data-tryon-fit-notes]");
    if (notesEl) {
      const notes = fit?.notes ?? [];
      notesEl.innerHTML = "";
      for (const note of notes) {
        const li = document.createElement("li");
        li.textContent = note;
        notesEl.appendChild(li);
      }
      notesEl.hidden = notes.length === 0;
    }

    this.setNotice("advice", this.hasSubject ? measurementAdvice(this.measurement) : null);
  }

  private get hasSubject(): boolean {
    return this.hasPhoto || this.mode === "camera";
  }

  /** Reflects state into the parts of the page the class does not draw. */
  private syncChrome(): void {
    const frame = this.selected;

    for (const thumb of this.root.querySelectorAll<HTMLElement>(".tryon-thumb")) {
      thumb.classList.toggle("is-selected", thumb.dataset.variantId === this.selectedId);
    }

    if (frame) {
      const set = (name: string, value: string) => {
        const el = this.find<HTMLElement>(`[data-tryon-selected="${name}"]`);
        if (el) el.textContent = value;
      };
      set("brand", frame.brand ?? "");
      set("name", frame.name);
      set("colour", frame.colorName);
      set("price", frame.priceText);

      const link = this.find<HTMLAnchorElement>("[data-tryon-choose]");
      if (link) link.href = `/frames/${frame.slug}?variant=${frame.variantId}`;
    }

    const cameraButton = this.find<HTMLButtonElement>("[data-tryon-camera]");
    if (cameraButton) {
      cameraButton.textContent = this.mode === "camera" ? "Stop camera" : "Use my camera";
      cameraButton.hidden = !this.config.cameraEnabled;
    }

    const manualButton = this.find<HTMLButtonElement>("[data-tryon-manual]");
    if (manualButton) {
      manualButton.textContent = this.manual ? "Done adjusting eyes" : "Adjust eye points";
      manualButton.hidden = !this.hasSubject;
    }

    const guidesButton = this.find<HTMLButtonElement>("[data-tryon-guides]");
    if (guidesButton) {
      guidesButton.textContent = this.showGuides ? "Hide guides" : "Show guides";
      guidesButton.hidden = !this.hasSubject;
    }

    const hint = this.find<HTMLElement>("[data-tryon-drag-hint]");
    if (hint) hint.hidden = !(this.manual && this.hasSubject);

    for (const selector of ["[data-tryon-actions]", "[data-tryon-adjust]"]) {
      const el = this.find<HTMLElement>(selector);
      if (el) el.hidden = !this.hasSubject;
    }

    const saveButton = this.find<HTMLButtonElement>("[data-tryon-save]");
    if (saveButton) saveButton.hidden = !this.config.canSave;

    this.canvas.classList.toggle("is-manual", this.manual);
    this.syncMeasurements();
  }
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.crossOrigin = "anonymous";
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error(`Could not load ${src}`));
    image.src = src;
  });
}
