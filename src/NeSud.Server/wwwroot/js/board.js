"use strict";

const buttons = [];
for (let i = 0; i < 81; i++) {
    let b = document.createElement("button");
    b.className = "cell";
    b.onclick = () => {
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
function enter(d, temp = false) {
    act("enter", { cell: selected, digit: d, note: temp || note })
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
