/* Product page enhancements.

   External rather than inline: the Content-Security-Policy forbids inline
   script, and an inline block was silently blocked by it. Progressive
   enhancement throughout — with scripting off the form still submits the
   selected colourway, and the try-on link still goes to a real frame. */
(function () {
  "use strict";

  var lead = document.getElementById("lead-image");
  var tryOn = document.querySelector("[data-product-tryon]");
  var choices = document.querySelectorAll('input[name="VariantId"]');

  if (!choices.length) return;

  choices.forEach(function (input) {
    input.addEventListener("change", function () {
      if (!input.checked) return;

      // The big picture follows the colour.
      if (lead && input.dataset.image) {
        lead.src = input.dataset.image;
        var name = input.parentElement.querySelector(".colour-name");
        if (name) lead.alt = lead.alt.replace(/ in .*$/, " in " + name.textContent.trim());
      }

      // So does the try-on: sending someone to the mirror and showing them a
      // different colour from the one they just picked is its own small
      // betrayal, and the artwork is per colourway anyway.
      if (tryOn && input.dataset.tryon) {
        tryOn.href = "/try-on?variant=" + encodeURIComponent(input.dataset.tryon);
      }
    });
  });
})();
