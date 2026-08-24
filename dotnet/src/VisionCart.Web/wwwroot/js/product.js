/* Product page enhancements.
   External rather than inline: the Content-Security-Policy forbids inline
   script, and an inline block was silently blocked by it. Progressive
   enhancement throughout — the form submits correctly with scripting off. */
(function () {
  "use strict";

  var lead = document.getElementById("lead-image");
  if (!lead) return;

  document.querySelectorAll('input[name="VariantId"]').forEach(function (input) {
    input.addEventListener("change", function () {
      var thumb = input.parentElement && input.parentElement.querySelector("img");
      if (thumb) lead.src = thumb.src;
    });
  });
})();
