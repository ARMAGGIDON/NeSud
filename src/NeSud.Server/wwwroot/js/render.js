"use strict";

function updateRevive() {
    if (!game?.outOfLives || game.finalDefeat || !online) return;
    const left = Math.max(0, Math.ceil((reviveEnd - performance.now()) / 1000));
    $("revive-progress").value = left;
    $("cover-detail").textContent = game.reward ? "Останній шанс використано.": left > 0 ? "Останній шанс: " + left + " с": "Час вичерпано.";
    $("resume").disabled = busy || adPending || left === 0;
    if (!left && !game.reward && online && !busy && !adPending) act("heartbeat")
}
setInterval(updateRevive, 200);
function updateClock() {
    const running = online && game && !game.paused && !game.completed && !game.outOfLives && !game.reward && !adPending;
    const seconds = (game?.seconds || 0) + (running ? Math.min(12, Math.floor((game.clockFraction || 0) + (performance.now() - clockAnchor) / 1000)): 0);
    $("time").textContent = fmt(seconds);
    $("score").textContent = Math.max(0, (game?.currentScore || 0) - Math.floor(seconds / 3) + Math.floor((game?.seconds || 0) / 3)).toLocaleString("uk-UA");
}
setInterval(updateClock, 200);
function render() {
    const active = online && game && !game.paused && !game.completed && !game.outOfLives && (!busy || !!pulse) && !adPending;
    $("cover").hidden = !!(online && game && !game.paused && !game.outOfLives);
    $("covertext").textContent = !online ? "Немає з’єднання": game?.outOfLives ? "Поразка": game?.paused ? "Гра на паузі": busy ? "Генеруємо поле…": "Створіть головоломку";
    $("resume").hidden = online && !game;
    $("resume").disabled = busy;
    $("resume").textContent = !online ? "Відновити з’єднання": game?.outOfLives ? "Реклама · останній шанс": "Продовжити";
    $("cover-detail").textContent = "";
    $("revive-progress").hidden = !game?.outOfLives;
    $("menu-continue").hidden = !regularGame;
    $("menu-continue").textContent = regularGame?.completed || regularGame?.finalDefeat
        ? "Останній результат" : "Продовжити гру";
    document.querySelectorAll("[data-new-level]").forEach(button => { button.disabled = !online || busy; });
    $("menu-challenge").disabled = guestMode || !online;
    $("menu-challenge").title = guestMode ? "Доступно після входу" : "";
    $("history-tab").disabled = guestMode || !online;
    $("history-tab").title = guestMode ? "Доступно після входу" : "";
    $("newgame").disabled = !online || busy;
    $("newgame").hidden = !!challengeDay && !!game && !game.finalDefeat && !game.completed;
    $("newgame").textContent = challengeDay ? (game ? "Повторити": "Почати щоденну гру"): "Нова головоломка";
    $("difficulty-label").hidden = !!challengeDay;
    $("game-mode").textContent = challengeDay ? "Щоденна гра · " + challengeDay: "Нова гра";
    $("level").textContent = game ? levels[game.difficulty]: "–";
    updateClock();
    $("lives").textContent = Array.from({ length: 3 }, (_, i) => i < (game?.lives || 0) ? "♥": "♡").join(" ");
    $("penalties").textContent = game ? "Час −" + Math.floor(game.seconds / 3) + " · Помилки −" + 75 * game.mistakes + " · Підказки −" + 150 * game.hints + " · Кандидати −" + game.effectiveCandidatePenalty + " · Життя −" + 100 * game.lifeRestores + " · Авто −" + game.autoFillPenalty: "";
    $("hint").textContent = "Підказка (" + (game?.hintCredits || 0) + ")";
    $("candidates").textContent = "Кандидати (" + (game?.candidateCredits || 0) + ")";
    $("candidates").title = game ? "Зміни: " + game.candidateDelta + " · −" + (2 * game.candidateDelta) + " балів": "";
    document.querySelectorAll("[data-reward]").forEach(b => {
        const count = b.dataset.reward === "life" ? game?.lives: b.dataset.reward === "hint" ? game?.hintCredits: game?.candidateCredits;
        b.hidden = !game || game.completed || game.outOfLives || count !== 0 || balance(b.dataset.reward) > 0;
        b.disabled = !online || busy || adPending
    });
    $("rewards").hidden = Array.from(document.querySelectorAll("[data-reward]")).every(b => b.hidden);
    $("result-daily").hidden = guestMode || !game?.completed || !daily?.packs.some(p => challengeDay ? p.milestone === "challenge": !["weekly", "challenge"].includes(p.milestone));
    $("challenge-rewards").hidden = guestMode || !daily?.packs.some(p => p.milestone === "challenge");
    $("weekly-rewards").hidden = guestMode || !daily?.packs.some(p => p.milestone === "weekly");
    $("progress").textContent = game ? (game.completed ? "✓ Судоку розв’язано! ": "") + game.values.filter(Boolean).length + "/81 · Підказки: " + game.hints: "";
    buttons.forEach((b, i) => {
        const v = game?.values[i] || 0, r = Math.floor(i / 9), c = i % 9, sr = Math.floor(selected / 9), sc = selected % 9;
        const animation = ["pop", "shake", "hint-pulse"].filter(k => b.classList.contains(k)).join(" ");
        b.className = "cell" + (c % 3 === 2 && c !== 8 ? " blockright": "") + (r % 3 === 2 && r !== 8 ? " blockbottom": "") + (game?.givens[i] ? " given": "") + (i === selected ? " selected": v && v === game?.values[selected] ? " same": r === sr || c === sc || Math.floor(r / 3) === Math.floor(sr / 3) && Math.floor(c / 3) === Math.floor(sc / 3) ? " peer": "");
        if (animation) b.classList.add(...animation.split(" "));
        b.classList.toggle("wrong", !!game?.wrong?.[i]);
        b.replaceChildren();
        if (v) b.textContent = v;
        else if (game?.notes[i]) {
            let n = document.createElement("span");
            n.className = "mininotes";
            for (let d = 1; d <= 9; d++) {
                let k = document.createElement("span");
                k.textContent = game.notes[i] & (1 << d) ? d: "";
                n.append(k)
            }b.append(n)
        }
        b.tabIndex = i === selected ? 0: -1;
        b.disabled = !online || !game || game.paused || game.outOfLives || adPending;
        b.setAttribute("aria-label", "Рядок " + (r + 1) + ", стовпець " + (c + 1) + ": " + (v || "порожньо"));
    });
    document.querySelectorAll("#pad button,#tools button").forEach(b => b.disabled = !active);
    Array.from($("pad").children).forEach((b, i) => {
        const n = game?.remaining?.[i] ?? 9;
        b.querySelector("small").textContent = n;
        b.classList.toggle("spent-digit", n === 0);
        b.disabled = !active || (!note && n === 0);
        b.setAttribute("aria-label", "Цифра " + (i + 1) + ", залишилось " + n)
    });
    $("auto-finish").checked = game?.autoFinish ?? autoFinish;
    $("auto-finish").disabled = !!game && (busy || game.completed || game.outOfLives);
    document.querySelector('[data-action="undo"]').disabled = !active || !game?.canUndo;
    document.querySelector('[data-action="redo"]').disabled = !active || !game?.canRedo;
    $("notes").classList.toggle("active", note);
    showResult();
    updateRevive();
}
document.addEventListener("keydown", e => {
    if (tab !== "play" || !online || !game || e.target.matches("input,select") || $("confirm").open || $("auth").open || $("ad").open || $("daily").open || $("help").open || adPending) return;
    let k = e.key.toLowerCase();
    if (k === "p" || k === "escape") {
        act(game.paused ? "resume": "pause");
        e.preventDefault();
        return
    }
    if (game.paused || game.completed || game.outOfLives) return;
    if (e.ctrlKey && k === "z") act(e.shiftKey ? "redo": "undo");
    else if (e.ctrlKey && k === "y") act("redo");
    else if (e.ctrlKey) return;
    else if (/^[1-9]$/.test(k)) enter(Number(k), e.shiftKey);
    else if (["delete", "backspace", "0"].includes(k)) enter(0);
    else if (k === "n" || k === " ") {
        note = !note;
        render()
    }else if (k === "h") tool("hint");
    else if (k === "a") tool("candidates");
    else if (k.startsWith("arrow")) {
        let r = Math.floor(selected / 9), c = selected % 9;
        if (k === "arrowleft") c--;
        if (k === "arrowright") c++;
        if (k === "arrowup") r--;
        if (k === "arrowdown") r++;
        selected = Math.max(0, Math.min(8, r)) * 9 + Math.max(0, Math.min(8, c));
        render();
        buttons[selected].focus()
    }else return;
    e.preventDefault()
});
