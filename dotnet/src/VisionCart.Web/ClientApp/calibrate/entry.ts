/**
 * The try-on calibration screen.
 *
 * Six numbers describe where a frame sits inside its own picture: the two lens
 * optical centres, the two edges of the frame front, and the top and bottom of
 * the lens opening. They used to be six text boxes, which is an accurate way to
 * store them and a hopeless way to enter them — nobody can look at a photograph
 * and say that the left lens centre is at 0.3173 of the width.
 *
 * So they are markers on the picture. Drag them, or focus one and use the arrow
 * keys; the hidden form fields follow. Nothing here is a matter of taste, and
 * the panel says continuously whether the marks and the millimetres agree,
 * because that is the only check that catches a marker put in the wrong place.
 */

type MarkerKey =
  | "leftLensCenterX" | "leftLensCenterY"
  | "rightLensCenterX" | "rightLensCenterY"
  | "frontLeftX" | "frontRightX"
  | "lensTopY" | "lensBottomY";

type Marks = Record<MarkerKey, number>;

type Config = {
  imageUrl: string | null;
  physical: {
    lensWidthMm: number | null;
    bridgeWidthMm: number | null;
    lensHeightMm: number | null;
    totalWidthMm: number | null;
  };
  markers: Partial<Marks>;
  tolerance: number;
};

/**
 * Where the markers start when a picture has never been calibrated.
 *
 * Deliberately not a guess at any particular frame: they are spread wide enough
 * to be visible and obviously wrong, so nobody mistakes an untouched screen for
 * a calibrated one.
 */
const FALLBACK: Marks = {
  leftLensCenterX: 0.3, leftLensCenterY: 0.5,
  rightLensCenterX: 0.7, rightLensCenterY: 0.5,
  frontLeftX: 0.14, frontRightX: 0.86,
  lensTopY: 0.25, lensBottomY: 0.75,
};

/** Arrow-key step, as a fraction of the image. Shift gives a tenth of it. */
const STEP = 0.005;
const FINE_STEP = 0.0005;

function boot(): void {
  const form = document.querySelector<HTMLFormElement>("[data-calibrate]");
  const script = document.getElementById("calibrate-config");
  if (!form || !script?.textContent) return;

  const config: Config = JSON.parse(script.textContent);
  const stage = form.querySelector<HTMLElement>("[data-calibrate-stage]");
  const image = form.querySelector<HTMLImageElement>("[data-calibrate-image]");
  if (!stage || !image) return;

  const marks: Marks = { ...FALLBACK };
  for (const [key, value] of Object.entries(config.markers)) {
    if (typeof value === "number" && Number.isFinite(value)) marks[key as MarkerKey] = value;
  }

  // --- writing back -------------------------------------------------------

  const field = (name: string) =>
    form.querySelector<HTMLInputElement>(`[data-cal-value="${name}"]`);

  function publish(): void {
    for (const [key, value] of Object.entries(marks)) {
      const input = field(key);
      if (input) input.value = value.toFixed(4);
    }
    const w = field("imageWidth");
    const h = field("imageHeight");
    if (w) w.value = String(image!.naturalWidth || "");
    if (h) h.value = String(image!.naturalHeight || "");
  }

  // --- placing the markers on screen --------------------------------------

  function layout(): void {
    const box = image!.getBoundingClientRect();
    const stageBox = stage!.getBoundingClientRect();
    const offsetX = box.left - stageBox.left;
    const offsetY = box.top - stageBox.top;

    const place = (el: HTMLElement, xFraction: number | null, yFraction: number | null) => {
      if (xFraction !== null) el.style.left = `${offsetX + xFraction * box.width}px`;
      if (yFraction !== null) el.style.top = `${offsetY + yFraction * box.height}px`;
    };

    const rails: Array<[string, MarkerKey, "x" | "y"]> = [
      ["frontLeftX", "frontLeftX", "x"],
      ["frontRightX", "frontRightX", "x"],
      ["lensTopY", "lensTopY", "y"],
      ["lensBottomY", "lensBottomY", "y"],
    ];

    for (const [selector, key, axis] of rails) {
      const el = stage!.querySelector<HTMLElement>(`[data-cal-marker="${selector}"]`);
      if (!el) continue;
      if (axis === "x") {
        place(el, marks[key], null);
        el.style.top = `${offsetY}px`;
        el.style.height = `${box.height}px`;
      } else {
        place(el, null, marks[key]);
        el.style.left = `${offsetX}px`;
        el.style.width = `${box.width}px`;
      }
    }

    for (const side of ["left", "right"] as const) {
      const el = stage!.querySelector<HTMLElement>(`[data-cal-marker="${side}LensCenter"]`);
      if (!el) continue;
      place(el, marks[`${side}LensCenterX` as MarkerKey], marks[`${side}LensCenterY` as MarkerKey]);
    }
  }

  // --- the running check --------------------------------------------------

  function review(): void {
    const { lensWidthMm, bridgeWidthMm, lensHeightMm, totalWidthMm } = config.physical;
    const w = image!.naturalWidth;
    const h = image!.naturalHeight;

    const centres = lensWidthMm && bridgeWidthMm
      ? (Math.abs(marks.rightLensCenterX - marks.leftLensCenterX) * w) / (lensWidthMm + bridgeWidthMm)
      : null;
    const front = totalWidthMm
      ? (Math.abs(marks.frontRightX - marks.frontLeftX) * w) / totalWidthMm
      : null;
    const aperture = lensHeightMm
      ? (Math.abs(marks.lensBottomY - marks.lensTopY) * h) / lensHeightMm
      : null;

    const show = (name: string, value: number | null) => {
      const el = form!.querySelector<HTMLElement>(`[data-cal-scale="${name}"]`);
      if (el) el.textContent = value ? `${value.toFixed(2)} px per mm` : "not enough data";
    };

    show("centres", centres);
    show("front", front);
    show("aperture", aperture);

    const readings = [centres, front, aperture].filter((v): v is number => v !== null && v > 0);
    const verdict = form!.querySelector<HTMLElement>("[data-cal-agreement]");
    if (!verdict) return;

    if (readings.length < 2) {
      verdict.textContent = "Add the missing measurements to this frame and the marks can be checked against them.";
      verdict.className = "cal-agreement is-unknown";
      return;
    }

    const spread = (Math.max(...readings) - Math.min(...readings)) / Math.max(...readings);

    if (spread <= config.tolerance) {
      verdict.textContent = `The marks and the measurements agree to within ${(spread * 100).toFixed(1)}%.`;
      verdict.className = "cal-agreement is-good";
    } else {
      verdict.textContent =
        `The marks and the measurements disagree by ${(spread * 100).toFixed(0)}%. `
        + "One of the markers is in the wrong place, or a measurement on the frame is wrong.";
      verdict.className = "cal-agreement is-bad";
    }
  }

  function refresh(): void {
    layout();
    publish();
    review();
  }

  // --- dragging -----------------------------------------------------------

  const clamp = (v: number) => Math.min(1, Math.max(0, v));

  function fractionsFromPointer(event: PointerEvent): { x: number; y: number } {
    const box = image!.getBoundingClientRect();
    return {
      x: clamp((event.clientX - box.left) / box.width),
      y: clamp((event.clientY - box.top) / box.height),
    };
  }

  for (const el of stage.querySelectorAll<HTMLElement>("[data-cal-marker]")) {
    const name = el.dataset.calMarker!;

    const applyPointer = (event: PointerEvent) => {
      const { x, y } = fractionsFromPointer(event);
      if (name.endsWith("X")) marks[name as MarkerKey] = x;
      else if (name.endsWith("Y")) marks[name as MarkerKey] = y;
      else {
        marks[`${name}X` as MarkerKey] = x;
        marks[`${name}Y` as MarkerKey] = y;
      }
      refresh();
    };

    el.addEventListener("pointerdown", event => {
      el.setPointerCapture(event.pointerId);
      el.classList.add("is-dragging");
      applyPointer(event);
      event.preventDefault();
    });

    el.addEventListener("pointermove", event => {
      if (el.hasPointerCapture(event.pointerId)) applyPointer(event);
    });

    el.addEventListener("pointerup", event => {
      el.releasePointerCapture(event.pointerId);
      el.classList.remove("is-dragging");
    });

    // The whole screen has to work without a mouse: a rail moves along its own
    // axis, a lens centre moves in both.
    el.addEventListener("keydown", event => {
      const step = event.shiftKey ? FINE_STEP : STEP;
      const move: Record<string, [number, number]> = {
        ArrowLeft: [-step, 0], ArrowRight: [step, 0],
        ArrowUp: [0, -step], ArrowDown: [0, step],
      };
      const delta = move[event.key];
      if (!delta) return;

      const [dx, dy] = delta;
      if (name.endsWith("X")) {
        if (dx === 0) return;
        marks[name as MarkerKey] = clamp(marks[name as MarkerKey] + dx);
      } else if (name.endsWith("Y")) {
        if (dy === 0) return;
        marks[name as MarkerKey] = clamp(marks[name as MarkerKey] + dy);
      } else {
        marks[`${name}X` as MarkerKey] = clamp(marks[`${name}X` as MarkerKey] + dx);
        marks[`${name}Y` as MarkerKey] = clamp(marks[`${name}Y` as MarkerKey] + dy);
      }

      event.preventDefault();
      refresh();
    });
  }

  // --- position from the measurements -------------------------------------
  // Most artwork is symmetric about its centre, so once the frame front is
  // marked the rest follows from the millimetres. This does not replace the
  // eye — it gets an untouched picture close enough to correct by hand.
  form.querySelector<HTMLButtonElement>("[data-cal-fit]")?.addEventListener("click", () => {
    const { lensWidthMm, bridgeWidthMm, lensHeightMm, totalWidthMm } = config.physical;
    if (!lensWidthMm || !bridgeWidthMm || !totalWidthMm) return;

    const frontSpan = marks.frontRightX - marks.frontLeftX;
    const perMm = frontSpan / totalWidthMm;
    const centre = (marks.frontLeftX + marks.frontRightX) / 2;
    const halfCentres = ((lensWidthMm + bridgeWidthMm) * perMm) / 2;

    marks.leftLensCenterX = clamp(centre - halfCentres);
    marks.rightLensCenterX = clamp(centre + halfCentres);

    if (lensHeightMm && image!.naturalWidth && image!.naturalHeight) {
      // The vertical scale is the horizontal one, since the picture is not
      // stretched: convert through the image's own aspect.
      const perMmY = (perMm * image!.naturalWidth) / image!.naturalHeight;
      const midY = (marks.lensTopY + marks.lensBottomY) / 2;
      const half = (lensHeightMm * perMmY) / 2;
      marks.lensTopY = clamp(midY - half);
      marks.lensBottomY = clamp(midY + half);
      marks.leftLensCenterY = midY;
      marks.rightLensCenterY = midY;
    }

    refresh();
  });

  // --- go -----------------------------------------------------------------

  if (image.complete && image.naturalWidth) refresh();
  else image.addEventListener("load", refresh, { once: true });

  window.addEventListener("resize", layout);
  form.addEventListener("submit", publish);
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", boot, { once: true });
} else {
  boot();
}
