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
        if (pulse) await pulse;
        if (!game || busy || !online || game.paused || game.completed || game.outOfLives || adPending) return;
        const digit = paintDigit || focusDigit || game.values[selected];
        const paint = paintMode && digit && !game.values[i] && !game.givens[i];
        if (paint) paintDigit = digit;
        selectCell(i);
        b.focus();
        if (paint) void act("enter", { cell: i, digit, note: note || event.shiftKey });
    };
    $("board").append(b);
    buttons.push(b)
}
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
function lockedCell() {
    return !!game && (!!game.givens[selected] || (!!game.values[selected] && !game.wrong[selected]));
}
function enter(d, temp = false) {
    if (!online || !game || game.paused || game.completed || game.outOfLives || adPending) return;
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
$("paint-digit").onchange = () => {
    paintMode = $("paint-digit").checked;
    paintDigit = paintMode ? (focusDigit || game?.values[selected] || 0) : 0;
    localStorage.setItem("paintDigit", paintMode ? "1" : "0");
    render();
};
