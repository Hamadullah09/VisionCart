/**
 * Shared helpers for the user-guide recordings.
 *
 * The videos have no soundtrack, so everything the viewer needs to understand
 * has to be on screen. Three pieces of furniture are injected into the page for
 * that, and nothing else about the application is altered:
 *
 *   - a caption bar that says, in plain English, what is happening;
 *   - a visible cursor, because a recorded browser does not draw the real one
 *     and a click that comes from nowhere is impossible to follow;
 *   - a title card between sections, so the video has chapters.
 *
 * Pacing is deliberately slow. A guide the viewer has to pause is a guide that
 * failed.
 */

/** Roughly the time it takes to read a short sentence, in milliseconds. */
export function readingTime(text) {
  const words = text.split(/\s+/).length;
  return Math.min(7000, Math.max(2200, 380 * words));
}

export const OVERLAY = `
  #vg-caption {
    position: fixed; left: 0; right: 0; bottom: 0; z-index: 2147483000;
    background: rgba(11,26,42,.94); color: #fff;
    font: 600 22px/1.45 "Segoe UI", system-ui, sans-serif;
    padding: 20px 40px 22px; text-align: center;
    letter-spacing: .005em;
    box-shadow: 0 -8px 30px rgba(0,0,0,.28);
    transform: translateY(110%); transition: transform .32s ease;
  }
  #vg-caption.on { transform: translateY(0); }
  #vg-caption small {
    display: block; margin-bottom: 5px;
    font: 600 12px/1 "Segoe UI", system-ui, sans-serif;
    letter-spacing: .16em; text-transform: uppercase; color: #7ab8ea;
  }

  #vg-cursor {
    position: fixed; z-index: 2147483001; width: 26px; height: 26px;
    margin: -13px 0 0 -13px; border-radius: 50%;
    border: 2.5px solid #0B5FA5; background: rgba(11,95,165,.22);
    pointer-events: none; opacity: 0;
    transition: left .45s cubic-bezier(.4,0,.2,1), top .45s cubic-bezier(.4,0,.2,1), opacity .2s;
  }
  #vg-cursor.on { opacity: 1; }
  #vg-cursor.tap { animation: vg-tap .45s ease-out; }
  @keyframes vg-tap {
    0%   { box-shadow: 0 0 0 0 rgba(11,95,165,.55); }
    100% { box-shadow: 0 0 0 26px rgba(11,95,165,0); }
  }

  #vg-title {
    position: fixed; inset: 0; z-index: 2147483002;
    background: #0B1A2A; color: #fff; display: flex; flex-direction: column;
    align-items: center; justify-content: center; gap: 14px;
    font-family: "Segoe UI", system-ui, sans-serif;
    opacity: 0; pointer-events: none; transition: opacity .4s ease;
  }
  #vg-title.on { opacity: 1; }
  #vg-title .eyebrow {
    font: 600 14px/1 "Segoe UI", system-ui, sans-serif;
    letter-spacing: .22em; text-transform: uppercase; color: #7ab8ea;
  }
  #vg-title .headline { font: 700 52px/1.15 "Segoe UI", system-ui, sans-serif; text-align: center; max-width: 20ch; }
  #vg-title .sub { font: 400 21px/1.5 "Segoe UI", system-ui, sans-serif; color: #b9c8d6; max-width: 34ch; text-align: center; }
`;

/**
 * Injected once per page load, styles and all.
 *
 * The stylesheet has to travel with the elements. Adding it separately after
 * load races every navigation, and a caption bar with no CSS is an invisible
 * div at the bottom of the document — which is exactly what the first take
 * recorded.
 */
export const FURNITURE = (css) => {
  // An init script runs before the document exists, so the elements cannot be
  // appended yet. The verbs are defined immediately — they look their elements
  // up when called — and the furniture is inserted as soon as there is a
  // document to insert it into.
  const install = () => {
    if (!document.documentElement) return false;
    if (document.getElementById("vg-caption")) return true;

    const style = document.createElement("style");
    style.id = "vg-style";
    style.textContent = css;
    document.documentElement.appendChild(style);

    const add = (id, html = "") => {
      const el = document.createElement("div");
      el.id = id;
      el.innerHTML = html;
      document.documentElement.appendChild(el);
    };

    add("vg-cursor");
    add("vg-caption");
    add("vg-title",
      '<div class="eyebrow"></div><div class="headline"></div><div class="sub"></div>');
    return true;
  };

  if (!install()) {
    document.addEventListener("DOMContentLoaded", install, { once: true });
  }

  const el = (id) => { install(); return document.getElementById(id); };

  window.__vg = {
    say(eyebrow, text) {
      const bar = el("vg-caption");
      if (!bar) return;
      bar.innerHTML = (eyebrow ? "<small>" + eyebrow + "</small>" : "") + text;
      bar.classList.add("on");
    },
    hush() { el("vg-caption")?.classList.remove("on"); },
    title(eyebrow, headline, sub) {
      const card = el("vg-title");
      if (!card) return;
      card.querySelector(".eyebrow").textContent = eyebrow;
      card.querySelector(".headline").textContent = headline;
      card.querySelector(".sub").textContent = sub || "";
      card.classList.add("on");
    },
    untitle() { el("vg-title")?.classList.remove("on"); },
    point(x, y) {
      const c = el("vg-cursor");
      if (!c) return;
      c.classList.add("on");
      c.style.left = x + "px";
      c.style.top = y + "px";
    },
    tap() {
      const c = el("vg-cursor");
      if (!c) return;
      c.classList.remove("tap");
      void c.offsetWidth;
      c.classList.add("tap");
    },
  };
};

/**
 * Wraps a Playwright page with the narration verbs used by every scene script.
 */
export function narrator(page, { chapter = "" } = {}) {
  const api = {
    chapter,

    /** Show a caption and hold it long enough to read. */
    async say(text, extra = 0) {
      if (process.env.VG_TRACE) console.log("    say:", text.slice(0, 52));
      await page.evaluate(([e, t]) => window.__vg?.say(e, t), [api.chapter, text]);
      await page.waitForTimeout(readingTime(text) + extra);
    },

    async hush(ms = 400) {
      await page.evaluate(() => window.__vg?.hush());
      await page.waitForTimeout(ms);
    },

    async titleCard(eyebrow, headline, sub, ms = 3200) {
      await page.evaluate(([e, h, s]) => window.__vg?.title(e, h, s), [eyebrow, headline, sub]);
      await page.waitForTimeout(ms);
      await page.evaluate(() => window.__vg?.untitle());
      await page.waitForTimeout(500);
    },

    /** Move the visible cursor onto a locator without clicking. */
    async moveTo(locator) {
      const box = await locator.boundingBox();
      if (!box) return null;
      const x = box.x + box.width / 2;
      const y = box.y + box.height / 2;
      await page.evaluate(([px, py]) => window.__vg?.point(px, py), [x, y]);
      await page.waitForTimeout(650);
      return { x, y };
    },

    /** Move, show a tap ripple, then really click. */
    async click(locator, { settle = 1100 } = {}) {
      await locator.scrollIntoViewIfNeeded().catch(() => {});
      await page.waitForTimeout(250);
      await api.moveTo(locator);
      await page.evaluate(() => window.__vg?.tap());
      await page.waitForTimeout(300);
      await locator.click({ timeout: 15000 });
      await page.waitForTimeout(settle);
    },

    /** Type into a field slowly enough to read. */
    async type(locator, text, { delay = 55 } = {}) {
      await api.moveTo(locator);
      await page.evaluate(() => window.__vg?.tap());
      await locator.click();
      await locator.fill("");
      await locator.type(text, { delay });
      await page.waitForTimeout(500);
    },

    async select(locator, value) {
      await api.moveTo(locator);
      await page.evaluate(() => window.__vg?.tap());
      await locator.selectOption(value);
      await page.waitForTimeout(900);
    },

    /** Scroll gently so the viewer can follow, rather than jumping. */
    async scroll(pixels, steps = 14) {
      const step = pixels / steps;
      for (let i = 0; i < steps; i++) {
        await page.mouse.wheel(0, step);
        await page.waitForTimeout(55);
      }
      await page.waitForTimeout(600);
    },

    async goto(url, caption) {
      if (caption) await api.say(caption);
      await page.goto(url, { waitUntil: "networkidle" });
      await page.waitForTimeout(900);
    },

    async beat(ms = 900) { await page.waitForTimeout(ms); },
  };

  return api;
}
