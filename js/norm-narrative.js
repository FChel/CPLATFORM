(function () {
  "use strict";

  function byId(id) { return document.getElementById(id); }
  var data = JSON.parse(byId("narrativeEditorData").textContent);
  var editor = byId("blockEditor"), destination = byId("blockDestination"), notice = byId("narrativeNotice");
  var type = byId("blockType"), placement = byId("blockPlacement"), sequence = byId("blockSequence"), status = byId("blockStatus");
  var current = null, dirty = false, selection = null;
  var names = { Disclosure: "Narrative disclosure", AccountingPolicy: "Accounting policy",
    EntityOverview: "Entity overview", SectionIntroduction: "Section introduction" };

  function option(select, value, text) {
    var item = document.createElement("option");
    item.value = value;
    item.textContent = text;
    select.appendChild(item);
  }
  (data.destinations || []).forEach(function (item) { option(destination, item.value, item.label); });

  function open(block) {
    current = block || null;
    selection = null;
    var value = block || {};
    byId("blockId").value = value.Id || "0";
    byId("blockEditToken").value = value.EditToken || "";
    destination.value = value.Kind ? value.Kind + "|" + value.Key : destination.options[0].value;
    type.value = value.Type || "Disclosure";
    placement.value = value.Placement || "Before";
    sequence.value = value.Sequence || 1;
    status.value = value.Status || "Draft";
    // The server rebuilds each fragment from the permitted tags and strips every attribute.
    editor.innerHTML = value.Html || "";
    byId("blockReview").textContent = value.RolledForward ? "Carried forward · annual review required" : "Awaiting review";
    dirty = false;
  }

  function confirmDiscard() {
    return !dirty || window.confirm("Discard unsaved changes to this block?");
  }

  (data.blocks || []).forEach(function (block) {
    var item = document.createElement("li");
    var button = document.createElement("button");
    var label = (data.destinations || []).filter(function (target) { return target.value === block.Kind + "|" + block.Key; })[0];
    button.type = "button";
    button.textContent = (label ? label.label : block.Kind + " " + block.Key) + " · " +
      (names[block.Type] || block.Type) + " · " + block.Placement.toLowerCase() + " " + block.Sequence +
      " · " + (block.RolledForward ? "Annual review" : block.Status);
    button.addEventListener("click", function () {
      if (confirmDiscard()) open(block);
    });
    item.appendChild(button);
    byId("narrativeList").appendChild(item);
  });
  open(data.pending || (data.blocks || [])[0]);

  [destination, type, placement, sequence, status].forEach(function (field) {
    field.disabled = !data.editable;
    field.addEventListener("change", function () { dirty = true; });
  });
  if (!data.editable) return;
  editor.addEventListener("input", function () { dirty = true; });
  editor.addEventListener("paste", function (event) {
    event.preventDefault();
    document.execCommand("insertText", false, event.clipboardData.getData("text/plain"));
  });
  editor.addEventListener("drop", function (event) { event.preventDefault(); });
  document.addEventListener("selectionchange", function () {
    var active = window.getSelection();
    if (active.rangeCount && editor.contains(active.anchorNode) && editor.contains(active.focusNode))
      selection = active.getRangeAt(0).cloneRange();
  });
  Array.prototype.forEach.call(document.querySelectorAll("[data-format]"), function (button) {
    button.addEventListener("mousedown", function (event) { event.preventDefault(); });
    button.addEventListener("click", function () {
      editor.focus();
      if (selection) {
        var active = window.getSelection();
        active.removeAllRanges();
        active.addRange(selection);
      }
      document.execCommand("styleWithCSS", false, false);
      if (document.execCommand(button.dataset.format, false, button.dataset.value || null)) dirty = true;
    });
  });
  byId("newNarrative").addEventListener("click", function () {
    if (confirmDiscard()) { open(null); editor.focus(); }
  });
  byId("cancelNarrative").addEventListener("click", function () {
    var original = (data.blocks || []).filter(function (block) { return current && block.Id === current.Id; })[0];
    open(original);
    notice.textContent = "Unsaved changes discarded.";
  });

  window.normSubmitNarrative = function () {
    if (!destination.value || !sequence.checkValidity() || !editor.textContent.trim()) {
      notice.textContent = "Select a destination, enter a sequence from 1 to 999, and enter narrative text.";
      return false;
    }
    byId("blockHtml").value = editor.innerHTML;
    return true;
  };
  window.normConfirmRollover = confirmDiscard;
  var prior = data.previous || [];
  if (prior.length) {
    prior.forEach(function (item) { option(byId("narrativeSource"), item.id, item.label); });
    byId("narrativeRollover").hidden = false;
  }
}());
