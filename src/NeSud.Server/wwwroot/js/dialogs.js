"use strict";

// All dismissals use the same cancellation path. Explicit confirmation buttons
// retain their own return values and reward claims retain their server checks.
function dismissDialog(dialog) {
    if (!dialog.open) return;
    const cancel = new Event("cancel", { cancelable: true });
    if (dialog.dispatchEvent(cancel)) dialog.close("cancel");
}

document.querySelectorAll("dialog").forEach(dialog => {
    let outsidePointer = null;
    const outside = event => {
        const rect = dialog.getBoundingClientRect();
        return event.clientX < rect.left || event.clientX >= rect.right ||
            event.clientY < rect.top || event.clientY >= rect.bottom;
    };

    dialog.querySelectorAll("[data-dismiss]").forEach(button => {
        button.addEventListener("click", () => dismissDialog(dialog));
    });

    dialog.addEventListener("pointerdown", event => {
        outsidePointer = event.target === dialog && outside(event) ? event.pointerId : null;
    });
    dialog.addEventListener("pointerup", event => {
        const dismiss = outsidePointer === event.pointerId && event.target === dialog && outside(event);
        outsidePointer = null;
        if (dismiss) dismissDialog(dialog);
    });
    dialog.addEventListener("pointercancel", () => { outsidePointer = null; });
    dialog.addEventListener("close", () => { outsidePointer = null; });
});
