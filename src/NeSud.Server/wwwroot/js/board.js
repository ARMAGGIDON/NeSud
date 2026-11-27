"use strict";

let focusDigit = 0;
const buttons = [];
for (let i = 0; i < 81; i++) {
    let b = document.createElement("button");
    b.className = "cell";
    b.onclick = () => {
        focusDigit = 0;
        selected = i;
        render();
        b.focus()
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
    if (focusDigit || lockedCell()) {
        if (!d) return;
        const next = nearestDigit(game.values, selected, d, !focusDigit);
        if (next >= 0) {
            focusDigit = 0;
            selected = next;
            render();
            buttons[next].focus();
        } else {
            focusDigit = d;
            render();
            $("pad").children[d - 1].focus();
        }
        return;
    }
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
