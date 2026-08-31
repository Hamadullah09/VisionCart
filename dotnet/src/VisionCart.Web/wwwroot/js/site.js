/* Site chrome.

   The navigation is a <details> rendered open, so with scripting off it is a
   plain stacked list of links rather than something unreachable. This folds it
   away on a narrow screen and keeps it open on a wide one — which is also why
   the desktop rule is here rather than in CSS: a closed <details> hides its own
   content in a way author CSS cannot reliably override. */
(function () {
  "use strict";

  var nav = document.querySelector("[data-site-nav]");
  if (!nav) return;

  var narrow = window.matchMedia("(max-width: 860px)");
  var opened = false;

  nav.addEventListener("toggle", function () {
    if (narrow.matches) opened = nav.open;
  });

  var sync = function () {
    nav.open = narrow.matches ? opened : true;
  };

  narrow.addEventListener("change", function () {
    opened = false;
    sync();
  });

  /* Following a link should not leave the menu hanging open behind the next
     page's scroll position on a phone. */
  nav.querySelectorAll("a").forEach(function (link) {
    link.addEventListener("click", function () { opened = false; });
  });

  sync();
})();
