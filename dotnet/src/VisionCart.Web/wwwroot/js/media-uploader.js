/* Bulk media uploader.
   External, because the Content-Security-Policy forbids inline script.

   Files are posted one at a time rather than as one multipart batch, for two
   reasons carried over from the legacy uploader: progress is meaningful per
   file, and a single corrupt image is reported by name while the rest of the
   shoot still goes through. */
(function () {
  "use strict";

  var root = document.querySelector("[data-uploader]");
  if (!root) return;

  var dropzone = root.querySelector("[data-dropzone]");
  var input = root.querySelector("[data-file-input]");
  var list = root.querySelector("[data-upload-list]");
  var tagsField = root.querySelector("[data-upload-tags]");
  var keepAlpha = root.querySelector("[data-keep-alpha]");
  var uploadUrl = root.getAttribute("data-upload-url");

  // The antiforgery token is a real hidden input inside the container, so read
  // it straight from the DOM. Without it every POST comes back 400.
  var tokenInput = root.querySelector('input[name="__RequestVerificationToken"]');
  var token = tokenInput ? tokenInput.value : "";

  var uploaded = 0;

  dropzone.addEventListener("click", function () { input.click(); });
  dropzone.addEventListener("keydown", function (e) {
    if (e.key === "Enter" || e.key === " ") { e.preventDefault(); input.click(); }
  });

  ["dragenter", "dragover"].forEach(function (name) {
    dropzone.addEventListener(name, function (e) {
      e.preventDefault();
      dropzone.classList.add("is-over");
    });
  });

  ["dragleave", "drop"].forEach(function (name) {
    dropzone.addEventListener(name, function (e) {
      e.preventDefault();
      dropzone.classList.remove("is-over");
    });
  });

  dropzone.addEventListener("drop", function (e) {
    if (e.dataTransfer && e.dataTransfer.files.length) queue(e.dataTransfer.files);
  });

  input.addEventListener("change", function () {
    if (input.files.length) queue(input.files);
    input.value = "";
  });

  function row(name) {
    var li = document.createElement("li");
    li.className = "upload-row";
    li.innerHTML =
      '<span class="upload-name"></span><span class="upload-state">Waiting…</span>';
    li.querySelector(".upload-name").textContent = name;
    list.hidden = false;
    list.append(li);
    return li.querySelector(".upload-state");
  }

  function queue(files) {
    // Sequential, not parallel: a shoot of 60 photos would otherwise open 60
    // connections and the per-file progress would be meaningless.
    var pending = Array.prototype.slice.call(files);
    var states = pending.map(function (file) { return row(file.name); });

    (function next(i) {
      if (i >= pending.length) return;
      var state = states[i];
      state.textContent = "Uploading…";
      state.className = "upload-state";

      send(pending[i], function (result) {
        if (result.ok) {
          state.textContent = "Done";
          state.className = "upload-state is-ok";
          uploaded++;
        } else {
          state.textContent = result.error || "Failed";
          state.className = "upload-state is-error";
        }
        next(i + 1);
      });
    })(0);
  }

  function send(file, done) {
    var body = new FormData();
    body.append("file", file);
    body.append("tags", tagsField ? tagsField.value : "");
    body.append("keepAlpha", keepAlpha && keepAlpha.checked ? "true" : "false");
    body.append("__RequestVerificationToken", token);

    fetch(uploadUrl, { method: "POST", body: body })
      .then(function (response) {
        return response.json().catch(function () { return { ok: false }; });
      })
      .then(function (payload) {
        done(payload);
        // Reload once the last file lands so the library below reflects reality.
        if (uploaded > 0) scheduleReload();
      })
      .catch(function () {
        done({ ok: false, error: "Upload failed." });
      });
  }

  var reloadTimer = null;
  function scheduleReload() {
    if (reloadTimer) clearTimeout(reloadTimer);
    reloadTimer = setTimeout(function () { window.location.reload(); }, 1200);
  }
})();
