document.addEventListener("click", function (e) {
    var btn = e.target.closest("[data-dup-act]");
    if (!btn) return;
    e.preventDefault();
    var form = btn.closest("form");
    if (!form) return;
    var confirmMsg = btn.getAttribute("data-dup-confirm");
    if (confirmMsg && !window.confirm(confirmMsg)) return;
    var action = form.querySelector("#dupAction");
    var recordId = form.querySelector("#dupRecordId");
    var moduleName = form.querySelector("#dupModuleName");
    if (action) action.value = btn.getAttribute("data-dup-act") || "";
    if (recordId) recordId.value = btn.getAttribute("data-dup-id") || "";
    if (moduleName) moduleName.value = btn.getAttribute("data-dup-mod") || "";
    if (typeof form.requestSubmit === "function") form.requestSubmit();
    else form.submit();
});
