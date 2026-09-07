/* Back-office shell: navigation, menus, toasts.

   External, because the Content-Security-Policy is `script-src 'self'` with no
   'unsafe-inline' — an inline <script> or an onclick attribute is silently
   dropped by the browser.

   Progressive enhancement only. Every page here works with scripting off: the
   drawer is only needed below 1024px where the sidebar is off-canvas, the
   menus are plain links and a POST form, and the toasts are already in the
   markup before this file runs. */
(function () {
  "use strict";

  var body = document.body;
  var STORE_KEY = "vc.admin.nav-compact";

  /* --- sidebar: collapse on desktop, drawer on small screens -------------- */

  // The collapsed rail is a preference, so it has to survive navigation.
  // Reading it can throw in a locked-down browser profile, so it is guarded.
  try {
    if (localStorage.getItem(STORE_KEY) === "1") body.classList.add("nav-compact");
  } catch (e) { /* storage unavailable — start expanded */ }

  var collapse = document.querySelector("[data-nav-collapse]");
  if (collapse) {
    collapse.addEventListener("click", function () {
      var compact = body.classList.toggle("nav-compact");
      collapse.setAttribute("aria-label", compact ? "Expand navigation" : "Collapse navigation");
      collapse.title = compact ? "Expand navigation" : "Collapse navigation";
      try { localStorage.setItem(STORE_KEY, compact ? "1" : "0"); } catch (e) { /* ignore */ }
    });
  }

  var burger = document.querySelector("[data-nav-open]");
  var scrim = document.querySelector("[data-nav-close]");
  var side = document.getElementById("admin-side");

  function setDrawer(open) {
    body.classList.toggle("nav-open", open);
    if (burger) burger.setAttribute("aria-expanded", open ? "true" : "false");
    if (scrim) scrim.hidden = !open;
    if (open && side) {
      var first = side.querySelector("a");
      if (first) first.focus();
    }
  }

  if (burger) burger.addEventListener("click", function () { setDrawer(true); });
  if (scrim) scrim.addEventListener("click", function () { setDrawer(false); });

  // Following a link inside the drawer should close it, or the new page is
  // rendered behind a scrim on a phone.
  if (side) {
    side.addEventListener("click", function (event) {
      if (event.target.closest("a") && window.matchMedia("(max-width: 1024px)").matches) {
        setDrawer(false);
      }
    });
  }

  /* --- menus -------------------------------------------------------------- */

  var openMenu = null;

  function closeMenu() {
    if (!openMenu) return;
    var panel = document.querySelector('[data-menu-panel="' + openMenu + '"]');
    var trigger = document.querySelector('[data-menu="' + openMenu + '"]');
    if (panel) panel.hidden = true;
    if (trigger) trigger.setAttribute("aria-expanded", "false");
    openMenu = null;
  }

  document.querySelectorAll("[data-menu]").forEach(function (trigger) {
    var name = trigger.getAttribute("data-menu");
    trigger.addEventListener("click", function (event) {
      event.stopPropagation();
      var panel = document.querySelector('[data-menu-panel="' + name + '"]');
      if (!panel) return;
      var wasOpen = openMenu === name;
      closeMenu();
      if (!wasOpen) {
        panel.hidden = false;
        trigger.setAttribute("aria-expanded", "true");
        openMenu = name;
      }
    });
  });

  // A click inside a menu should not close it before the link is followed.
  document.querySelectorAll("[data-menu-panel]").forEach(function (panel) {
    panel.addEventListener("click", function (event) { event.stopPropagation(); });
  });

  document.addEventListener("click", closeMenu);

  document.addEventListener("keydown", function (event) {
    if (event.key === "Escape") {
      closeMenu();
      if (body.classList.contains("nav-open")) setDrawer(false);
    }
    // Ctrl/Cmd-K is the search shortcut the keyboard hint in the bar promises.
    if ((event.ctrlKey || event.metaKey) && (event.key === "k" || event.key === "K")) {
      var search = document.querySelector("[data-search]");
      if (search) {
        event.preventDefault();
        search.focus();
        search.select();
      }
    }
  });

  /* --- toasts ------------------------------------------------------------- */

  document.querySelectorAll("[data-toast]").forEach(function (toast) {
    var close = document.createElement("button");
    close.type = "button";
    close.className = "toast-close";
    close.setAttribute("aria-label", "Dismiss");
    close.innerHTML = "&times;";
    toast.appendChild(close);

    var timer = null;

    function dismiss() {
      if (timer) clearTimeout(timer);
      toast.classList.add("is-going");
      toast.addEventListener("animationend", function () { toast.remove(); }, { once: true });
    }

    close.addEventListener("click", dismiss);

    // An error is left on screen: it usually says something the operator has
    // to act on, and a five-second window is not long enough to read it.
    if (!toast.classList.contains("alert-error")) {
      timer = setTimeout(dismiss, 6000);
      // Reading it should not be a race against the timer.
      toast.addEventListener("mouseenter", function () { if (timer) clearTimeout(timer); });
      toast.addEventListener("mouseleave", function () { timer = setTimeout(dismiss, 2500); });
    }
  });

  /* --- responsive tables --------------------------------------------------
     Below 720px a `.stacks` table turns each row into a card, and every cell
     needs the column name beside it. Copying the header text here means a view
     author never has to repeat it as a data-label on every <td>. */

  document.querySelectorAll(".admin-table.stacks table").forEach(function (table) {
    var heads = [].map.call(table.querySelectorAll("thead th"), function (th) {
      return (th.textContent || "").trim();
    });
    if (!heads.length) return;
    table.querySelectorAll("tbody tr").forEach(function (row) {
      [].forEach.call(row.children, function (cell, i) {
        if (heads[i] && !cell.hasAttribute("data-label")) cell.setAttribute("data-label", heads[i]);
      });
    });
  });

  /* --- destructive actions ------------------------------------------------
     A confirmation the operator can read, rather than the browser's own
     dialog with a URL in the title bar. Falls back to confirm() where <dialog>
     is unsupported, and to submitting normally where script is off. */

  var dialog = null;

  function ensureDialog() {
    if (dialog) return dialog;
    if (!window.HTMLDialogElement) return null;
    dialog = document.createElement("dialog");
    dialog.className = "modal";
    dialog.innerHTML =
      '<form method="dialog">' +
      '<div class="modal-head">' +
      '<span class="modal-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9v4M12 17h.01"/><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg></span>' +
      '<div><h2 data-dialog-title>Are you sure?</h2><p data-dialog-body></p></div>' +
      "</div>" +
      '<div class="modal-foot">' +
      '<button class="btn btn-outline" value="cancel">Cancel</button>' +
      '<button class="btn btn-primary" value="ok" data-dialog-ok>Confirm</button>' +
      "</div></form>";
    document.body.appendChild(dialog);
    return dialog;
  }

  document.querySelectorAll("[data-confirm]").forEach(function (el) {
    el.addEventListener("click", function (event) {
      if (el.dataset.confirmed === "1") { delete el.dataset.confirmed; return; }

      var message = el.getAttribute("data-confirm") || "This cannot be undone.";
      var label = el.getAttribute("data-confirm-action") || "Confirm";
      var box = ensureDialog();

      if (!box) {
        if (!window.confirm(message)) event.preventDefault();
        return;
      }

      event.preventDefault();
      box.querySelector("[data-dialog-title]").textContent = el.getAttribute("data-confirm-title") || "Are you sure?";
      box.querySelector("[data-dialog-body]").textContent = message;
      var ok = box.querySelector("[data-dialog-ok]");
      ok.textContent = label;
      ok.classList.toggle("btn-danger", el.classList.contains("btn-danger"));
      ok.classList.toggle("btn-primary", !el.classList.contains("btn-danger"));

      box.returnValue = "";
      box.showModal();

      box.addEventListener("close", function () {
        if (box.returnValue !== "ok") return;
        el.dataset.confirmed = "1";
        // Re-issue the original action now that it has been agreed to.
        if (el.tagName === "BUTTON" && el.form) {
          if (el.name) {
            var carry = document.createElement("input");
            carry.type = "hidden";
            carry.name = el.name;
            carry.value = el.value;
            el.form.appendChild(carry);
          }
          el.form.requestSubmit ? el.form.requestSubmit() : el.form.submit();
        } else {
          el.click();
        }
      }, { once: true });
    });
  });

  /* --- busy buttons -------------------------------------------------------
     A form that takes a moment should not look like it ignored the click. */

  document.querySelectorAll("form").forEach(function (form) {
    form.addEventListener("submit", function () {
      var button = form.querySelector('button[type="submit"], button:not([type])');
      if (!button || button.classList.contains("is-busy")) return;
      // Only after the browser has accepted the submission, so a validation
      // failure does not leave a spinner running.
      window.setTimeout(function () {
        if (!form.checkValidity || form.checkValidity()) button.classList.add("is-busy");
      }, 0);
    });
  });
})();
