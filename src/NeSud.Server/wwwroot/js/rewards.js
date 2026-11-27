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
            if ($("daily").open && !revealed && !questAnimating && !dailyBusy) drawDaily()
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
let automaticRewardDay = null;
let questRange = null;
let questAnimating = false;
let rewardAttempt = null;
let pendingRewardGame = null;
function resourceIcon(kind) {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("aria-hidden", "true");
    svg.classList.add("resource-icon");
    const path = document.createElementNS(svg.namespaceURI, "path");
    path.setAttribute("d", kind === "hint" ? "M9 18h6m-5 3h4M8 16v-2C1 8 6 1 12 1s11 7 4 13v2z" : "M3 3h18v18H3zM9 3v18m6-18v18M3 9h18M3 15h18");
    svg.append(path);
    return svg;
}
function giftArt() {
    const art = document.createElement("span");
    art.className = "gift-art";
    for (const part of ["gift-box", "gift-prize", "gift-lid"]) {
        const element = document.createElement("span");
        element.className = part;
        art.append(element);
    }
    return art;
}
async function showVictoryReward(id, day, e) {
    if (guestMode || !online || !token) return;
    const key = e + ":" + id;
    if (pendingRewardGame === key) return;
    pendingRewardGame = key;
    const baseline = daily;
    try {
        if (dailyTask) await dailyTask;
        if (epoch !== e || game?.id !== id || !online) return;
        const updated = await api("daily");
        if (epoch !== e || game?.id !== id) return;
        daily = updated;
        render();
    } catch { return; }
    if (epoch !== e || game?.id !== id || !game.completed || tab !== "play" || document.querySelector("dialog[open]")) return;
    if (!daily) return;
    dailyMode = day ? "challenge" : "win";
    automaticRewardDay = day;
    questRange = null;
    if (day) {
        if (!daily.packs.some(matchesPack)) return;
    } else {
        const to = Math.min(7, daily.wins);
        const from = baseline ? (baseline.day === daily.day ? Math.min(7, baseline.wins) : 0) : Math.max(0, to - 1);
        if ((!baseline && daily.wins > 7) || to <= from) return;
        questRange = { day: daily.day, from, to };
    }
    revealed = false;
    rewardAttempt = null;
    $("daily-result").textContent = "";
    $("daily").showModal();
    if (questRange) {
        questAnimating = true;
        try { await animateQuest(questRange.from, questRange.to); }
        finally { questAnimating = false; }
        if (!$("daily").open || epoch !== e) return;
        if (!daily.packs.some(matchesPack)) { $("daily").close(); return; }
    }
    drawDaily();
}
async function animateQuest(from, to) {
    const host = $("daily-cards");
    host.replaceChildren();
    host.classList.add("quest-animation");
    const title = document.createElement("h2"), count = document.createElement("strong"), track = document.createElement("div"), fill = document.createElement("div");
    title.textContent = "Щоденний квест";
    count.textContent = from + " / 7";
    count.className = "quest-count";
    count.setAttribute("role", "status");
    track.className = "quest-track";
    track.setAttribute("role", "progressbar");
    track.setAttribute("aria-label", "Щоденний квест");
    track.setAttribute("aria-valuemin", "0");
    track.setAttribute("aria-valuemax", "7");
    track.setAttribute("aria-valuenow", String(from));
    fill.className = "quest-fill";
    fill.style.width = (from / 7 * 100) + "%";
    track.append(fill);
    const marks = document.createElement("div");
    marks.className = "quest-marks";
    for (const n of [1, 3, 7]) {
        const mark = document.createElement("span");
        mark.textContent = n;
        mark.style.left = (n / 7 * 100) + "%";
        mark.classList.toggle("reached", n <= from);
        marks.append(mark);
    }
    host.append(title, count, track, marks);
    const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
    void fill.offsetWidth;
    fill.style.width = (to / 7 * 100) + "%";
    if (!reduced) await new Promise(resolve => setTimeout(resolve, 650));
    count.textContent = to + " / 7";
    track.setAttribute("aria-valuenow", String(to));
    Array.from(marks.children).forEach(mark => mark.classList.toggle("reached", Number(mark.textContent) <= to));
    await new Promise(resolve => setTimeout(resolve, reduced ? 100 : 350));
    host.classList.remove("quest-animation");
}
function matchesPack(p) {
    if (questRange) {
        const milestone = { win1: 1, win3: 3, win7: 7 }[p.milestone] || 0;
        return p.day === questRange.day && milestone > questRange.from && milestone <= questRange.to;
    }
    return dailyMode === "weekly" ? p.milestone === "weekly": dailyMode === "challenge" ? p.milestone === "challenge" && (!automaticRewardDay || p.day === automaticRewardDay): !["weekly", "challenge"].includes(p.milestone)
}
function drawDaily() {
    $("daily-heading").textContent = dailyMode === "weekly" ? "Призи турніру": dailyMode === "challenge" ? "Нагороди щоденних ігор": "Щоденні нагороди";
    compactMetrics($("daily-summary"), daily ? [
        ...(dailyMode === "win" ? [["Перемоги сьогодні", Math.min(7, daily.wins) + " / 7"]] : []),
        ["Доступно наборів", daily.packs.filter(matchesPack).length]] : [["Стан", "Підключення…"]]);
    const wallet = $("wallet"), cards = $("daily-cards");
    wallet.replaceChildren();
    cards.replaceChildren();
    if (!daily) return;
    const pack = daily.packs.find(matchesPack);
    $("daily-milestone").textContent = !pack ? "Усі доступні нагороди отримано.": ({ login: "Попередня нагорода за вхід", win1: "Перша перемога", win3: "Третя перемога", win7: "Сьома перемога", win: "Перемога " + (pack.slot + 1), weekly: "Тижневий турнір", challenge: "Щоденна гра" })[pack.milestone] + " (" + pack.day + ")";
    if (!pack) {
        const empty = document.createElement("p");
        empty.textContent = "Немає доступних нагород.";
        const close = document.createElement("button");
        close.textContent = "Готово";
        close.onclick = () => $("daily").close();
        cards.append(empty, close);
        return;
    }
    for (let i = 0; i < 3; i++) {
        const b = document.createElement("button");
        b.className = "mystery-card";
        b.append(giftArt());
        const caption = document.createElement("span");
        caption.className = "gift-caption";
        caption.textContent = "";
        b.append(caption);
        b.setAttribute("aria-label", "Відкрити подарунок " + (i + 1));
        b.disabled = dailyBusy || (rewardAttempt !== null && rewardAttempt !== i);
        b.onclick = () => chooseCard(pack, i);
        cards.append(b)
    }
}
async function chooseCard(pack, card) {
    if (dailyBusy || (rewardAttempt !== null && rewardAttempt !== card)) return;
    rewardAttempt = card;
    dailyBusy = true;
    const e = epoch;
    drawDaily();
    try {
        const r = await api("daily/choose", { ...pack, card });
        if (e !== epoch) return;
        daily = r.status;
        if (!$("daily").open) return;
        revealed = true;
        const cards = $("daily-cards");
        Array.from(cards.children).forEach((b, i) => {
            if (i === card) {
                b.querySelector(".gift-prize").append(resourceIcon(r.item.kind));
                b.querySelector(".gift-caption").textContent = "×" + r.item.amount;
                b.classList.add("revealed");
            } else b.classList.add("unchosen");
            b.disabled = true
        });
        $("daily-result").textContent = "Отримано: " + resourceNames[r.item.kind] + " ×" + r.item.amount;
        const next = document.createElement("button");
        next.className = "primary next-card";
        next.textContent = daily.packs.some(matchesPack) ? "Далі" : "Готово";
        next.onclick = () => {
            if (!daily.packs.some(matchesPack)) { $("daily").close(); return; }
            revealed = false;
            rewardAttempt = null;
            $("daily-result").textContent = "";
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
$("daily").addEventListener("cancel", e => { if (dailyBusy || questAnimating) e.preventDefault(); });
$("daily-open").onclick = async() => {
    if (guestMode) {
        showAuth();
        return
    }
    if (dailyMode === "win" && !game?.completed) return;
    if (!await pauseForNavigation()) return;
    if ($("daily").open) return;
    automaticRewardDay = null;
    questRange = null;
    rewardAttempt = null;
    revealed = false;
    $("daily-result").textContent = "";
    $("daily").showModal();
    drawDaily();
    await refreshDaily()
};

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
