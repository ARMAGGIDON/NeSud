"use strict";

let focusDigit = 0, paintDigit = 0;
function selectCell(i) {
    focusDigit = 0;
    selected = i;
    if (paintMode && game?.values[i]) paintDigit = game.values[i];
    render();
}
const buttons = [];
for (let i = 0; i < 81; i++) {
    let b = document.createElement("button");
    b.className = "cell";
    b.onclick = async event => {
        if (paintMode && event.detail > 0) return; // Pointer strokes are handled by the board.
        if (pulse) await pulse;
        if (!game || busy || !online || game.paused || game.completed || game.outOfLives || adPending) return;
        const digit = paintDigit || focusDigit || game.values[selected];
        const paint = paintMode && digit && game.remaining[digit - 1] > 0 && !game.values[i] && !game.givens[i];
        if (paint) paintDigit = digit;
        selectCell(i);
        b.focus();
        if (paint) void act("enter", { cell: i, digit, note: note || event.shiftKey });
    };
    $("board").append(b);
    buttons.push(b)
}
// The pointer belongs to the board for the entire stroke. Network moves are serialized.
const paintBoard = $("board");
let stroke = null, drainingStroke = false;
const strokeQueue = [];
function strokeCell(x, y) {
    const b = document.elementFromPoint(x, y)?.closest(".cell");
    return b ? buttons.indexOf(b) : -1;
}
function queueStrokePoint(s, x, y) {
    const i = strokeCell(x, y);
    if (i >= 0 && !s.seen.has(i)) {
        s.seen.add(i);
        strokeQueue.push({ s, i, first: s.seen.size === 1 });
    }
}
function sampleStroke(s, x, y) {
    if (s.last) {
        const [px, py] = s.last;
        const step = Math.max(1, Math.min(paintBoard.clientWidth, paintBoard.clientHeight) / 36);
        const n = Math.max(1, Math.ceil(Math.hypot(x - px, y - py) / step));
        for (let k = 1; k <= n; k++) queueStrokePoint(s, px + (x - px) * k / n, py + (y - py) * k / n);
    } else queueStrokePoint(s, x, y);
    s.last = [x, y];
    void drainStroke();
}
function validStroke(s) {
    return !s.cancelled && paintMode && epoch === s.epoch && tab === "play" && game?.id === s.id &&
        online && !game.paused && !game.completed && !game.outOfLives && !game.reward && !adPending;
}
async function drainStroke() {
    if (drainingStroke) return;
    drainingStroke = true;
    try {
        while (strokeQueue.length) {
            const { s, i, first } = strokeQueue.shift();
            if (pulse) await pulse;
            if (!validStroke(s) || busy) continue;
            if (first) {
                if (game.values[i]) s.digit = game.values[i];
                selectCell(i);
                buttons[i].focus();
            }
            if (!s.digit || !game.remaining[s.digit - 1] || game.values[i] || game.givens[i]) continue;
            paintDigit = s.digit;
            selectCell(i);
            buttons[i].focus();
            if (!await act("enter", { cell: i, digit: s.digit, note: s.note })) s.cancelled = true;
        }
    } finally { drainingStroke = false; }
}
paintBoard.addEventListener("pointerdown", e => {
    if (!paintMode || e.button !== 0 || !e.isPrimary) return;
    e.preventDefault();
    if (stroke || drainingStroke || !game || (busy && !pulse) || !online || game.paused ||
        game.completed || game.outOfLives || game.reward || adPending) return;
    stroke = { pointer: e.pointerId, epoch, id: game.id, digit: paintDigit || focusDigit || game.values[selected],
        note: note || e.shiftKey, seen: new Set(), last: null, cancelled: false };
    paintBoard.setPointerCapture(e.pointerId);
    sampleStroke(stroke, e.clientX, e.clientY);
});
paintBoard.addEventListener("pointermove", e => {
    if (stroke?.pointer !== e.pointerId) return;
    if (!(e.buttons & 1)) { finishStroke(e, false); return; }
    sampleStroke(stroke, e.clientX, e.clientY);
});
function finishStroke(e, cancel) {
    if (stroke?.pointer !== e.pointerId) return;
    const s = stroke;
    if (cancel) s.cancelled = true;
    else sampleStroke(s, e.clientX, e.clientY);
    stroke = null;
    if (paintBoard.hasPointerCapture(e.pointerId)) paintBoard.releasePointerCapture(e.pointerId);
}
paintBoard.addEventListener("pointerup", e => finishStroke(e, false));
paintBoard.addEventListener("pointercancel", e => finishStroke(e, true));
paintBoard.addEventListener("lostpointercapture", e => finishStroke(e, true));

for (let d = 1; d <= 9; d++) {
    const b = document.createElement("button");
    const num = document.createElement("strong"), count = document.createElement("small");
    num.textContent = d;
    count.textContent = "9";
    b.append(num, count);
    b.onclick = () => enter(d);
    $("pad").append(b)
}
function nearestDigit(values, anchor, digit, skipAnchor = true) {
    let best = -1, distance = Infinity;
    for (let i = 0; i < values.length; i++) {
        if (values[i] !== digit || (skipAnchor && i === anchor)) continue;
        const d = Math.abs(Math.floor(i / 9) - Math.floor(anchor / 9)) + Math.abs(i % 9 - anchor % 9);
        if (d < distance) { best = i; distance = d; }
    }
    return best >= 0 ? best : values[anchor] === digit ? anchor : -1;
}
function nextDigit(digit, remaining) {
    for (let step = 1; step < 9; step++) {
        const next = (digit - 1 + step) % 9 + 1;
        if (remaining[next - 1] > 0) return next;
    }
    return 0;
}
function advanceCompletedDigit(before, anchor) {
    if (!game || game.completed || game.outOfLives || before?.id !== game.id) return false;
    const digit = game.values[anchor];
    if (!digit || before.values[anchor] === digit || !before.remaining[digit - 1] || game.remaining[digit - 1] !== 0) return false;
    const next = nextDigit(digit, game.remaining);
    if (!next) return false;
    const target = nearestDigit(game.values, anchor, next, false);
    selected = target < 0 ? anchor : target;
    focusDigit = target < 0 ? next : 0;
    if (paintMode) paintDigit = next;
    return true;
}
function lockedCell() {
    return !!game && (!!game.givens[selected] || (!!game.values[selected] && !game.wrong[selected]));
}
function enter(d, temp = false) {
    if (!online || !game || game.paused || game.completed || game.outOfLives || adPending) return;
    if (d > 0 && game.remaining[d - 1] === 0) return;
    if (paintMode && d > 0) paintDigit = d;
    if (focusDigit || lockedCell()) {
        if (!d) return;
        const next = nearestDigit(game.values, selected, d, !focusDigit);
        if (next >= 0) {
            selectCell(next);
            buttons[next].focus();
        } else {
            focusDigit = d;
            render();
            $("pad").children[d - 1].focus();
        }
        return;
    }
    if (paintMode) render();
    act("enter", { cell: selected, digit: d, note: temp || note });
}
$("notes").onclick = () => {
    note = !note;
    render()
};
function tool(action) {
    if (action === "hint" && game?.hintCredits === 0) return chooseResource("hint");
    if (action === "candidates" && game?.candidateCredits === 0) return chooseResource("candidates");
    return act(action)
}
document.querySelectorAll("[data-action]").forEach(b => b.onclick = () => b.dataset.action === "erase" ? enter(0): tool(b.dataset.action));

for (const [id, key, apply] of [
    ["highlight-peers", "highlightPeers", value => { highlightPeers = value; }],
    ["auto-clean", "autoClean", value => { autoClean = value; }]
]) {
    $(id).checked = localStorage.getItem(key) !== "0";
    $(id).onchange = () => {
        const value = $(id).checked;
        localStorage.setItem(key, value ? "1" : "0");
        apply(value);
        render();
    };
}

$("paint-digit").checked = paintMode;
paintBoard.classList.toggle("paint-mode", paintMode);
$("paint-digit").onchange = () => {
    paintMode = $("paint-digit").checked;
    paintBoard.classList.toggle("paint-mode", paintMode);
    paintDigit = paintMode ? (focusDigit || game?.values[selected] || 0) : 0;
    localStorage.setItem("paintDigit", paintMode ? "1" : "0");
    render();
};
