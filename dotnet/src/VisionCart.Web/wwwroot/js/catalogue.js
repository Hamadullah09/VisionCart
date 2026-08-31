/* Catalogue filtering.

   Progressive enhancement: without scripting the form is an ordinary GET with
   a "Show frames" button and everything works. With it, changing a dropdown
   applies straight away, which is what people expect of a shop.

   Text and number fields are deliberately left alone — submitting on every
   keystroke of a search box or a price would be unusable. */
(function () {
  "use strict";

  var form = document.querySelector("[data-catalogue-form]");
  var panel = document.querySelector("[data-catalogue-filters]");
  if (!form) return;

  form.querySelectorAll("select").forEach(function (select) {
    select.addEventListener("change", function () {
      form.submit();
    });
  });

  /* On a phone the filters start folded so the frames are the first thing on
     screen; on a wide screen they are always open and the summary is hidden.
     Doing it here rather than in CSS keeps `open` correct after a back button. */
  if (panel) {
    var narrow = window.matchMedia("(max-width: 860px)");

    var sync = function () {
      // Leave it open on a narrow screen if the visitor opened it themselves.
      if (narrow.matches && !panel.dataset.touched) panel.open = false;
      if (!narrow.matches) panel.open = true;
    };

    panel.addEventListener("toggle", function () {
      if (narrow.matches) panel.dataset.touched = "1";
    });

    narrow.addEventListener("change", sync);
    sync();
  }
})();
