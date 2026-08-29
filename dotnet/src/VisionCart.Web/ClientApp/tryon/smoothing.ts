/**
 * Temporal smoothing for the try-on.
 *
 * Landmark detection is noisy: a perfectly still head still produces pupils
 * that wander a pixel or two every frame, and a frame drawn straight from raw
 * output shivers. The obvious fix — average the last few frames — trades that
 * shiver for lag, and lag is worse: the glasses swim behind the face whenever
 * anybody moves.
 *
 * The One Euro filter resolves the trade rather than picking a side. It adapts
 * its own cutoff to how fast the signal is moving: heavy smoothing while the
 * head is still, almost none while it moves. That is precisely the behaviour a
 * face tracker wants, and it is why it is named here rather than a plain
 * exponential average.
 *
 *   Casiez, Roussel & Vogel (2012), "1€ Filter: A Simple Speed-based
 *   Low-pass Filter for Noisy Input in Interactive Systems".
 *
 * Nothing in this file touches the DOM or the mesh, so every claim above is
 * testable by feeding it numbers.
 */

/** Smoothing at rest. Lower is steadier and laggier. */
export const DEFAULT_MIN_CUTOFF = 1.0;

/** How sharply the filter opens up as the signal accelerates. */
export const DEFAULT_BETA = 0.35;

/** Cutoff for the derivative estimate itself, which is noisier than the signal. */
export const DEFAULT_DERIVATIVE_CUTOFF = 1.0;

export type OneEuroOptions = {
  minCutoff?: number;
  beta?: number;
  derivativeCutoff?: number;
};

/** Smooths one scalar over time. */
export class OneEuroFilter {
  private readonly minCutoff: number;
  private readonly beta: number;
  private readonly dCutoff: number;

  private previous: number | null = null;
  private derivative = 0;
  private lastSeconds: number | null = null;

  constructor(options: OneEuroOptions = {}) {
    this.minCutoff = options.minCutoff ?? DEFAULT_MIN_CUTOFF;
    this.beta = options.beta ?? DEFAULT_BETA;
    this.dCutoff = options.derivativeCutoff ?? DEFAULT_DERIVATIVE_CUTOFF;
  }

  /** @param seconds monotonic timestamp; the filter needs real elapsed time. */
  filter(value: number, seconds: number): number {
    if (this.previous === null || this.lastSeconds === null) {
      this.previous = value;
      this.lastSeconds = seconds;
      return value;
    }

    const dt = seconds - this.lastSeconds;

    // A zero or backwards step would divide by zero and throw the filter into
    // nonsense; the previous answer is the honest response to no new time.
    if (!(dt > 0)) return this.previous;

    const rate = 1 / dt;

    const rawDerivative = (value - this.previous) * rate;
    this.derivative = lowPass(rawDerivative, this.derivative, alpha(this.dCutoff, rate));

    // The adaptive part: the faster it moves, the wider the cutoff opens.
    const cutoff = this.minCutoff + this.beta * Math.abs(this.derivative);
    const smoothed = lowPass(value, this.previous, alpha(cutoff, rate));

    this.previous = smoothed;
    this.lastSeconds = seconds;
    return smoothed;
  }

  /** Forgets its history, so the next sample is taken as truth. */
  reset(): void {
    this.previous = null;
    this.derivative = 0;
    this.lastSeconds = null;
  }

  get isPrimed(): boolean {
    return this.previous !== null;
  }
}

/** The handful of numbers that place a frame, smoothed together. */
export type SmoothedPose = {
  leftX: number; leftY: number;
  rightX: number; rightY: number;
  yawDeg: number; pitchDeg: number;
};

/**
 * Holds one filter per quantity.
 *
 * Angles get a lower beta than positions: a head rotates more slowly than it
 * translates, so the same responsiveness would only let noise through.
 */
export class PoseSmoother {
  private readonly filters: Record<keyof SmoothedPose, OneEuroFilter>;

  constructor(options: OneEuroOptions = {}) {
    const position = () => new OneEuroFilter(options);
    const angle = () => new OneEuroFilter({ ...options, beta: (options.beta ?? DEFAULT_BETA) * 0.5 });

    this.filters = {
      leftX: position(), leftY: position(),
      rightX: position(), rightY: position(),
      yawDeg: angle(), pitchDeg: angle(),
    };
  }

  smooth(pose: SmoothedPose, seconds: number): SmoothedPose {
    return {
      leftX: this.filters.leftX.filter(pose.leftX, seconds),
      leftY: this.filters.leftY.filter(pose.leftY, seconds),
      rightX: this.filters.rightX.filter(pose.rightX, seconds),
      rightY: this.filters.rightY.filter(pose.rightY, seconds),
      yawDeg: this.filters.yawDeg.filter(pose.yawDeg, seconds),
      pitchDeg: this.filters.pitchDeg.filter(pose.pitchDeg, seconds),
    };
  }

  /** Called when the subject changes — a new photo must not blend into the old. */
  reset(): void {
    for (const f of Object.values(this.filters)) f.reset();
  }
}

/**
 * Decides what to draw when detection drops out.
 *
 * Detection fails for a frame or two constantly — a blink, a hand, a turn past
 * the model's limit. Hiding the frame on the first miss makes it strobe;
 * holding it forever leaves glasses floating over an empty chair.
 *
 * So: hold the last good pose briefly, then fade rather than cut.
 */
export const HOLD_MS = 320;
export const FADE_MS = 260;

export type HoldState = {
  /** 0..1 — multiply the frame's opacity by this. */
  opacity: number;
  /** True while the last known pose should still be drawn. */
  usePrevious: boolean;
};

export function holdThroughLoss(msSinceLastDetection: number): HoldState {
  if (msSinceLastDetection <= HOLD_MS) return { opacity: 1, usePrevious: true };

  const fading = msSinceLastDetection - HOLD_MS;
  if (fading >= FADE_MS) return { opacity: 0, usePrevious: false };

  return { opacity: 1 - fading / FADE_MS, usePrevious: true };
}

function alpha(cutoff: number, rate: number): number {
  const tau = 1 / (2 * Math.PI * cutoff);
  return 1 / (1 + tau * rate);
}

function lowPass(value: number, previous: number, a: number): number {
  return a * value + (1 - a) * previous;
}
