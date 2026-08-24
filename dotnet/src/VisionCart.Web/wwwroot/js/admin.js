/* Back-office enhancements. External, because the Content-Security-Policy
   forbids inline script. Progressive enhancement only — every form here works
   with scripting disabled. */
(function () {
  "use strict";

  // Reveal the reject-reason row for a prescription.
  document.querySelectorAll("[data-reject-toggle]").forEach(function (button) {
    button.addEventListener("click", function () {
      var id = button.getAttribute("data-reject-toggle");
      var row = document.querySelector('[data-reject-form="' + id + '"]');
      if (row) row.hidden = !row.hidden;
    });
  });

  // Reveal an inline editor panel (lens options, delivery rates).
  document.querySelectorAll("[data-edit-toggle]").forEach(function (button) {
    button.addEventListener("click", function () {
      var id = button.getAttribute("data-edit-toggle");
      var panel = document.querySelector('[data-edit-panel="' + id + '"]');
      if (!panel) return;
      panel.hidden = !panel.hidden;
      if (!panel.hidden) {
        var first = panel.querySelector("input, select, textarea");
        if (first) first.focus();
      }
    });
  });
})();
