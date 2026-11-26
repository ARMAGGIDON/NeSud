"use strict";

function accept(data) {
    previousGame = game;
    game = data?.empty ? null : data;
    if (!challengeDay) regularGame = game;
    online = navigator.onLine;
    clockAnchor = performance.now();
    if (game) {
        selected = Math.max(0, Math.min(80, selected));
        reviveEnd = performance.now() + (game.reviveRemainingMs || 0);
        if (previousGame?.id !== game.id) {
            resultKey = "";
            comparison = null;
            $("result-difficulty").value = String(game.difficulty)
        }
        if (game.completed) loadComparison();
    }
    render();
    if (game && previousGame?.id === game.id) {
        for (let i = 0; i < 81; i++) if (game.values[i] !== previousGame.values[i] && game.values[i]) animate(buttons[i], game.wrong[i] ? "shake": "pop");
        if (game.lives < previousGame.lives) animate($("lives"), "heart-loss");
    }
}
async function reconnect() {
    if (busy || !token) return;
    const e = epoch;
    busy = true;
    try {
        const data = await gameSnapshot(challengeDay, tab === "play");
        if (e !== epoch) return;
        accept(data);
        if (tab === "play" && game?.reward && !$("ad").open) {
            const cleared = await gameMove({ id: game.id, revision: game.revision, challengeDay, action: "reward-cancel" });
            if (e !== epoch) return;
            accept(cleared);
        }
        if (tab === "play" && game && !game.paused && !game.completed && !game.outOfLives) {
            const paused = await gameMove({ id: game.id, revision: game.revision, challengeDay, action: "pause" });
            if (e !== epoch) return;
            accept(paused);
        }message("");
    }
    catch (e) {
        if (challengeDay && e.status === 400) {
            challengeDay = null;
            game = null;
            online = true;
            busy = false;
            await navigate("challenge");
            message(e.message)
        }else if (token) disconnect(e);
        else authError(e.message)
    }finally {
        busy = false;
        render()
    }
}
function disconnect(e) {
    online = false;
    message("Гру призупинено: " + e.message + ". Очікуємо з’єднання.");
    render()
}
window.addEventListener("offline", () => disconnect(Error("немає мережі")));
document.addEventListener("visibilitychange", () => {
    if (document.hidden && game && !game.paused && !game.completed && !game.outOfLives && !adPending) pauseForNavigation()
});
window.addEventListener("online", () => token ? reconnect(): login(true));
async function act(action, extra = { }) {
    if (action !== "heartbeat" && pulse) await pulse;
    if (!online || busy || !game) return false;
    const e = epoch;
    busy = true;
    try {
        const data = await gameMove({ id: game.id, revision: game.revision, challengeDay, action, ...extra });
        if (e !== epoch) return;
        if (data.pulseOnly) {
            game = { ...game, ...data };
            if (!challengeDay) regularGame = game;
            clockAnchor = performance.now();
            updateClock();
            return true
        }accept(data);
        if (action === "hint") {
            selected = game.selected;
            animate(buttons[selected], "hint-pulse")
        }
        if (game.completed && !previousGame?.completed && !guestMode) refreshDaily();
        message("");
        return true;
    }
    catch (e) {
        if (e.status === 409) { queueGameRefresh(challengeDay); message(e.message); }
        else if (e.status === 400) message(e.message);
        else disconnect(e);
        return false
    }finally {
        busy = false;
        if (action !== "heartbeat" || !online || game?.outOfLives || game?.paused || game?.completed || game?.reward) render()
    }
}
setInterval(() => {
    if (busy || authBusy || adBusy || pulse) return;
    if (!token) {
        if (navigator.onLine && !$("auth").open && Date.now() >= nextGuest) login(true);
        return
    }
    if (!online) {
        reconnect();
        return
    }
    if (tab === "play" && game && !game.completed && !game.finalDefeat && (!game.paused || game.reward || game.outOfLives)) {
        pulse = act("heartbeat").finally(() => {
            pulse = null
        })
    }
}, 6000);

function confirmNew() {
    return new Promise(resolve => {
        const d = $("confirm");
        d.querySelector("p").textContent = guestMode ? "Поточну демонстраційну гру буде замінено.": "Поточна незавершена гра залишиться в історії.";
        d.returnValue = "cancel";
        d.addEventListener("close", () => resolve(d.returnValue === "yes"), { once: true });
        d.showModal()
    })
}
$("newgame").onclick = async() => {
    if (pulse) await pulse;
    if (!online || busy || adPending) return;
    if (game && !game.completed && !game.finalDefeat && !(await confirmNew())) return;
    if (!online || busy) return;
    const e = epoch;
    busy = true;
    render();
    message("Генерація головоломки…");
    try {
        const data = await gameNew({ difficulty: Number($("difficulty").value), id: game?.id ?? null, revision: game?.revision ?? 0, autoFinish, challengeDay });
        if (e !== epoch) return;
        selected = data.selected;
        accept(data);
        message("");
    }
    catch (e) {
        if (e.status === 409) { queueGameRefresh(challengeDay); message(e.message); }
        else if (e.status === 400) message(e.message);
        else disconnect(e)
    }finally {
        busy = false;
        render()
    }
};
$("resume").onclick = () => !online ? (token ? reconnect(): login(true)): game?.outOfLives ? (game.reviveSeconds > 0 ? reward("life"): $("newgame").onclick()): act("resume");
