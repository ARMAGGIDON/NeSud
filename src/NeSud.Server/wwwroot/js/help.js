"use strict";

const helpTabs = document.querySelectorAll("[data-help]");
const helpPages = document.querySelectorAll("[data-help-page]");
function selectHelpPage(name) {
    helpTabs.forEach(button => button.setAttribute("aria-pressed", String(button.dataset.help === name)));
    helpPages.forEach(page => { page.hidden = page.dataset.helpPage !== name; });
    $("help-content").scrollTop = 0;
}
helpTabs.forEach(button => button.addEventListener("click", () => selectHelpPage(button.dataset.help)));

const helpRows = ["530070000", "600195000", "098000060", "800060003", "400803001",
    "700020006", "060000280", "000419005", "000080079"];
helpRows.forEach((row, r) => {
    [...row].forEach((digit, c) => {
        const cell = document.createElement("span");
        cell.className = "help-cell";
        const selected = r === 0 && c === 2;
        const wrong = r === 1 && c === 1;
        const peer = r === 0 || c === 2 || (r < 3 && c < 3);
        if (selected) cell.classList.add("selected");
        else if (wrong) cell.classList.add("wrong");
        else if (peer) cell.classList.add("peer");
        if ((c + 1) % 3 === 0) cell.classList.add("blockright");
        if ((r + 1) % 3 === 0) cell.classList.add("blockbottom");
        cell.textContent = selected ? "1 2 4" : wrong ? "6" : digit === "0" ? "" : digit;
        $("help-board").append(cell);
    });
});
$("help-open").onclick = async () => {
    if (await pauseForNavigation()) {
        selectHelpPage("board");
        $("help").showModal();
    }
};
$("help-close").onclick = () => $("help").close();
