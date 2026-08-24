import { TryOnStudio, type StudioConfig } from "./studio.ts";

/**
 * Boots the virtual mirror from configuration the Razor view embedded as JSON.
 *
 * Nothing here reaches the network except the model and WebAssembly runtime,
 * both served from this application's own wwwroot.
 */
function boot(): void {
  const root = document.querySelector<HTMLElement>("[data-tryon-root]");
  if (!root) return;

  const configScript = document.querySelector<HTMLScriptElement>("#tryon-config");
  if (!configScript?.textContent) return;

  try {
    const config = JSON.parse(configScript.textContent) as StudioConfig;
    new TryOnStudio(root, config);
  } catch (error) {
    console.error("[try-on] could not start the studio", error);
    const notice = root.querySelector<HTMLElement>('[data-tryon-notice="error"]');
    if (notice) {
      notice.textContent =
        "The virtual try-on could not start in this browser. Please browse the frames instead.";
      notice.hidden = false;
    }
  }
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", boot);
} else {
  boot();
}
