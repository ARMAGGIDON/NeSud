"use strict";

const rewardNames = { life: "Одне життя", hint: "Одна підказка", candidates: "Одне використання кандидатів" };
const resourceNames = { life: "Життя", hint: "Підказки", candidates: "Кандидати" };
const balance = k => daily?.[k === "life" ? "lives": k === "hint" ? "hints": "candidates"] || 0;
async function refreshDaily() {
    if (guestMode || !token || dailyBusy || !online) return;
    if (dailyTask) return dailyTask;
    const e = epoch;
    dailyTask = (async() => {
        try {
            const d = await api("daily");
            if (e !== epoch) return;
            daily = d;
            render();
            if ($("daily").open && !revealed) drawDaily()
        }catch { }finally {
            dailyTask = null
        }
    })();
    return dailyTask;
}
async function chooseResource(kind) {
    if (!guestMode) {
        await refreshDaily();
        if (balance(kind) > 0) {
            await spend(kind);
            return
        }
    }
    await reward(kind)
}
async function spend(kind) {
    if (busy || dailyBusy || !game || !online || guestMode) return;
    if (await act("wallet", { reward: kind })) {
        message("");
        await refreshDaily()
    }
}
function matchesPack(p) {
    return dailyMode === "weekly" ? p.milestone === "weekly": dailyMode === "challenge" ? p.milestone === "challenge": !["weekly", "challenge"].includes(p.milestone)
}
function drawDaily() {
    $("daily-heading").textContent = dailyMode === "weekly" ? "Призи турніру": dailyMode === "challenge" ? "Нагороди щоденних ігор": "Щоденні нагороди";
    $("daily-summary").textContent = daily ? (dailyMode === "win" ? "Сьогодні: " + Math.min(7, daily.wins) + "/7 перемог · ": "") + "Доступно наборів: " + daily.packs.filter(matchesPack).length: "Підключення…";
    const wallet = $("wallet"), cards = $("daily-cards");
    wallet.replaceChildren();
    cards.replaceChildren();
    if (!daily) return;
    for (const kind of ["hint", "candidates"]) {
        const b = document.createElement("button");
        b.textContent = resourceNames[kind] + ": " + balance(kind) + " · використати";
        const count = kind === "life" ? game?.lives: kind === "hint" ? game?.hintCredits: game?.candidateCredits;
        b.disabled = !game || game.completed || count >= 3 || !balance(kind) || dailyBusy;
        b.onclick = async() => {
            b.disabled = true;
            await spend(kind);
            drawDaily()
        };
        wallet.append(b)
    }
    const pack = daily.packs.find(matchesPack);
    $("daily-milestone").textContent = !pack ? "Усі доступні нагороди отримано.": ({ login: "Попередня нагорода за вхід", win1: "Перша перемога", win3: "Третя перемога", win7: "Сьома перемога", win: "Перемога " + (pack.slot + 1), weekly: "Тижневий турнір", challenge: "Щоденна гра" })[pack.milestone] + " · " + pack.day;
    if (!pack) return;
    for (let i = 0; i < 3; i++) {
        const b = document.createElement("button");
        b.className = "mystery-card";
        b.textContent = "?";
        b.setAttribute("aria-label", "Вибрати картку " + (i + 1));
        b.disabled = dailyBusy;
        b.onclick = () => chooseCard(pack, i);
        cards.append(b)
    }
}
async function chooseCard(pack, card) {
    if (dailyBusy) return;
    dailyBusy = true;
    drawDaily();
    try {
        const r = await api("daily/choose", { ...pack, card });
        daily = r.status;
        revealed = true;
        const cards = $("daily-cards");
        Array.from(cards.children).forEach((b, i) => {
            b.textContent = i === card ? resourceNames[r.item.kind] + " ×" + r.item.amount: "?";
            b.classList.toggle("revealed", i === card);
            b.disabled = true
        });
        $("daily-result").textContent = "Отримано: " + resourceNames[r.item.kind] + " ×" + r.item.amount;
        const next = document.createElement("button");
        next.className = "primary next-card";
        next.textContent = "Далі";
        next.onclick = () => {
            revealed = false;
            drawDaily()
        };
        cards.append(next);
    }
    catch (e) {
        $("daily-result").textContent = e.message
    }
    finally {
        dailyBusy = false;
        if (!revealed) drawDaily();
        render()
    }
}
$("daily-open").onclick = async() => {
    if (guestMode) {
        showAuth();
        return
    }
    if (dailyMode === "win" && !game?.completed) return;
    if (!await pauseForNavigation()) return;
    revealed = false;
    $("daily-result").textContent = "";
    $("daily").showModal();
    drawDaily();
    await refreshDaily()
};
$("daily-close").onclick = () => $("daily").close();

function adRender() {
    $("ad-time").textContent = adRemaining > 0 ? adRemaining + " с": "Готово!";
    $("ad-progress").value = Math.max(0, 8 - adRemaining);
    $("ad-claim").disabled = adRemaining > 0 || adBusy || busy || !online
}
async function reward(kind) {
    if (pulse) await pulse;
    if (!online || busy || adPending || !game || game.completed || kind !== "life" && ((kind === "hint" ? game.hintCredits: game.candidateCredits) > 0 || balance(kind) > 0) || game.outOfLives && kind !== "life" || kind === "life" && (!game.outOfLives || !game.reviveSeconds)) return;
    adPending = true;
    if (game.reward && game.reward.kind !== kind) {
        if (!await act("reward-cancel")) {
            adPending = false;
            return
        }
    }
    if (!await act("reward-start", { reward: kind })) {
        adPending = false;
        render();
        return
    }
    adKind = kind;
    adRemaining = game.reward?.remaining ?? 8;
    $("ad-kind").textContent = rewardNames[kind];
    const deadline = performance.now() + adRemaining * 1000;
    adRender();
    $("ad").showModal();
    adClock = setInterval(() => {
        adRemaining = Math.max(0, Math.ceil((deadline - performance.now()) / 1000));
        adRender()
    }, 100);
}
$("ad-claim").onclick = async() => {
    if (adRemaining > 0 || adBusy || busy) return;
    adBusy = true;
    adRender();
    const ok = await act("reward-claim", { reward: adKind });
    adBusy = false;
    if (ok) {
        message("");
        $("ad").close()
    }else adRender()
};
$("ad-cancel").onclick = () => $("ad").close();
$("ad").addEventListener("cancel", e => {
    if (adBusy) e.preventDefault()
});
$("ad").addEventListener("close", async() => {
    clearInterval(adClock);
    if (game?.reward && online) {
        while (busy && online) await new Promise(resolve => setTimeout(resolve, 50));
        if (online && game?.reward) await act("reward-cancel")
    }adPending = false;
    render()
});
document.querySelectorAll("[data-reward]").forEach(b => b.onclick = () => reward(b.dataset.reward));
