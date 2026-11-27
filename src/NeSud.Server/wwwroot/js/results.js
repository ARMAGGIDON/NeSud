"use strict";

function fmt(s) {
    return Math.floor(s / 60).toString().padStart(2, "0") + ":" + String(s % 60).padStart(2, "0")
}
function animate(el, name) {
    if (!el || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    el.classList.remove(name);
    void el.offsetWidth;
    el.classList.add(name);
    el.addEventListener("animationend", () => el.classList.remove(name), { once: true })
}
$("auto-finish").checked = autoFinish;
$("auto-finish").onchange = async() => {
    const value = $("auto-finish").checked;
    autoFinish = value;
    localStorage.setItem("autoFinish", value ? "1": "0");
    if (game && !game.completed && !game.outOfLives) {
        if (!await act("auto-finish", { note: value })) {
            autoFinish = !!game.autoFinish;
            localStorage.setItem("autoFinish", autoFinish ? "1": "0");
            $("auto-finish").checked = autoFinish
        }
    }
};
$("result-new").onclick = () => {
    if (challengeDay) {
        navigate("challenge");
        return
    }$("difficulty").value = $("result-difficulty").value;
    $("newgame").onclick()
};
$("result-rank").onclick = () => navigate("ranking");
$("result-daily").onclick = () => {
    dailyMode = challengeDay ? "challenge": "win";
    $("daily-open").onclick()
};
$("weekly-rewards").onclick = () => {
    dailyMode = "weekly";
    $("daily-open").onclick()
};
async function loadComparison() {
    if (!game?.completed) return;
    const id = game.id, e = epoch, key = e + ":" + id;
    if (resultKey === key) return;
    resultKey = key;
    comparison = null;
    try {
        const data = await api("comparison?difficulty=" + levelsEnum[game.difficulty] + "&score=" + game.currentScore + "&seconds=" + game.seconds + "&rules=" + game.rulesVersion);
        if (!data) throw Error("Порожня відповідь сервера.");
        if (e === epoch && game?.id === id && resultKey === key) {
            comparison = data;
            render();
        }
    }
    catch {
        if (e === epoch && game?.id === id && resultKey === key) {
            comparison = { failed: true };
            render();
        }
    }
}
const levelsEnum = ["Easy", "Medium", "Hard", "Expert"];
function resultRow(parent, label, value) {
    const r = document.createElement("div"), l = document.createElement("span"), v = document.createElement("strong");
    l.textContent = label;
    v.textContent = value;
    r.append(l, v);
    parent.append(r)
}
function loadingIcon(label) {
    const icon = document.createElement("span");
    icon.className = "loading-indicator";
    icon.tabIndex = 0;
    icon.setAttribute("role", "status");
    icon.setAttribute("aria-label", label);
    icon.dataset.tooltip = label;
    return icon;
}
let comparisonView = null;
function showResult() {
    const done = !!game?.completed, lost = !!game?.finalDefeat && !adPending;
    $("result").hidden = !(done || lost);
    $("game-panel").hidden = done || lost;
    $("game-tools").hidden = done || lost;
    if (!done && !lost) return;
    $("result-icon").textContent = done ? "✓": "♡";
    $("result-title").textContent = done ? "Головоломку розв’язано!": "Поразка";
    $("result-level").textContent = challengeDay ? "Щоденна гра (" + challengeDay + "), " + levels[game.difficulty] : levels[game.difficulty];
    $("result-difficulty").hidden = !!challengeDay;
    $("result-new").textContent = challengeDay ? "До календаря": "Нова головоломка";
    $("result-score").textContent = (done ? game.currentScore: 0).toLocaleString("uk-UA") + " балів";
    compactMetrics($("result-time"), [["Час", fmt(game.seconds)], ["Помилки", game.mistakes]]);
    const b = $("result-breakdown");
    b.replaceChildren();
    for (const [label, val] of [["База", game.baseScore], ["Бонус за швидкість", "+" + game.speedBonus], ["Час", "−" + Math.floor(game.seconds / 3)], ["Помилки", "−" + 75 * game.mistakes], ["Підказки", "−" + 150 * game.hints], ["Кандидати", "−" + game.effectiveCandidatePenalty], ["Автозавершення (" + game.autoFilled + " клітинок)", "−" + game.autoFillPenalty], ["Відновлення життя", "−" + 100 * game.lifeRestores]]) resultRow(b, label, val);
    const c = $("comparison");
    const view = [epoch, game.id, done, comparison, online];
    if (comparisonView?.every((value, i) => value === view[i])) return;
    comparisonView = view;
    c.replaceChildren();
    $("comparison-heading").hidden = !done;
    c.hidden = !done;
    c.setAttribute("aria-busy", String(done && !comparison && online));
    if (!done) return;
    if (!comparison || comparison.failed) {
        if (!comparison && online) {
            c.append(loadingIcon("Завантаження статистики…"));
        } else {
            const retry = document.createElement("button");
            retry.type = "button";
            retry.className = "comparison-retry";
            retry.textContent = "↻";
            const label = "Не вдалося отримати статистику. Повторити запит";
            retry.title = label;
            retry.setAttribute("aria-label", label);
            retry.disabled = !online;
            retry.onclick = () => {
                resultKey = "";
                void loadComparison();
                render();
            };
            c.append(retry);
        }
        return;
    }
    if (!comparison.games) {
        c.textContent = "Поки недостатньо результатів.";
        return;
    }
    for (const [label, val] of [["Ігор / гравців", comparison.games + " / " + comparison.players], ["Середній час", fmt(Math.round(comparison.averageSeconds))], ["Медіанний час", fmt(Math.round(comparison.medianSeconds))], ["Найкращий час", fmt(comparison.bestSeconds)], ["Середній бал", Math.round(comparison.averageScore)], ["Середні помилки", comparison.averageMistakes.toFixed(1)], ["Середні підказки", comparison.averageHints.toFixed(1)], ["Середні зміни кандидатів", comparison.averageCandidates.toFixed(1)], ["Ваш бал не нижче ніж у", comparison.scorePercentile + "% ігор"], ["Ваш час не довше ніж у", comparison.timePercentile + "% ігор"]]) resultRow(c, label, val)
}
