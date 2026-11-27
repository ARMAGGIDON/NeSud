"use strict";

// Closed single-select controls behave alike on mouse wheels and precision trackpads.
// Keep native dropdowns, keyboard focus, and the existing change handlers intact.
function installSelectWheel(root) {
    const states = new WeakMap();
    function onWheel(event) {
        const select = event.target.closest?.("select");
        if (!select || select.multiple || select.size > 1 || select.matches(":disabled") ||
            event.ctrlKey || event.shiftKey || event.altKey || event.metaKey ||
            !event.deltaY || Math.abs(event.deltaX) > Math.abs(event.deltaY)) return;
        // Native open pickers handle their own scrolling. :open is not available in older browsers.
        try { if (select.matches(":open")) return; } catch { }
        if (!event.cancelable) return;
        event.preventDefault();
        const delta = event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? 120 : 1);
        const state = states.get(select) || { delta: 0, time: 0 };
        if (event.timeStamp - state.time > 250 || Math.sign(state.delta) !== Math.sign(delta)) state.delta = 0;
        state.time = event.timeStamp;
        state.delta += delta;
        states.set(select, state);
        if (Math.abs(state.delta) < 40) return;
        const direction = Math.sign(state.delta);
        state.delta = 0;
        let index = select.selectedIndex;
        if (index < 0) index = direction > 0 ? -1 : select.options.length;
        for (let i = index + direction; i >= 0 && i < select.options.length; i += direction) {
            const option = select.options[i];
            if (option.disabled || option.hidden || option.parentElement?.disabled || option.parentElement?.hidden) continue;
            select.selectedIndex = i;
            select.dispatchEvent(new Event("input", { bubbles: true }));
            select.dispatchEvent(new Event("change", { bubbles: true }));
            break;
        }
    }
    root.addEventListener("wheel", onWheel, { passive: false });
    root.addEventListener("pointerout", event => {
        const select = event.target.closest?.("select");
        if (select && (!event.relatedTarget || !select.contains(event.relatedTarget))) states.delete(select);
    });
}
installSelectWheel(document);
