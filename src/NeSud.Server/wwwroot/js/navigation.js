"use strict";

function row(parent, values) {
    let r = document.createElement("div");
    r.className = "record";
    values.forEach(v => {
        let s = document.createElement("span");
        if (Array.isArray(v)) {
            v.forEach((part, index) => {
                const line = document.createElement(index === 0 ? "span" : "small");
                line.textContent = part;
                s.append(line);
            });
            s.classList.add("stacked-cell");
        } else s.textContent = v;
        r.append(s)
    });
    parent.append(r)
}
function emptyList(target, message) {
    const empty = document.createElement("p");
    empty.className = "empty-list";
    empty.textContent = message;
    target.append(empty);
}
let rankingRequest = 0, rankingFilterTimer = null;
const accumulatedHeaders = ["Перемоги / ідеальні", "Серія / рекорд", "Бали"];
const weeklyHeaders = ["Перемоги", "Бали"];
function activeRankMetric(metric) {
    return metric === "score" ? 2 : metric === "wins" || metric === "perfect" ? 0 : 1;
}
function rankHeading(parent, headers, active) {
    row(parent, ["№", "Гравець", ...headers]);
    const heading = parent.lastElementChild;
    heading.classList.add("rank-row", "rank-heading");
    if (active >= 0) heading.children[active + 2].classList.add("rank-metric");
}
function rankState(parent, kind, label, retry = null) {
    const state = document.createElement("div");
    state.className = "rank-state rank-state-" + kind;
    if (kind === "loading") state.append(loadingIcon(label));
    else {
        const text = document.createElement("p");
        text.textContent = label;
        state.append(text);
        if (retry) {
            const button = document.createElement("button");
            button.type = "button";
            button.textContent = "Повторити";
            button.onclick = retry;
            state.append(button);
        }
    }
    parent.append(state);
}
function prepareRank(target, headers, active, label, reset = false) {
    if (reset) delete target.dataset.signature;
    if (!target.dataset.signature && (reset || !target.querySelector(".rank-state-loading"))) {
        const fragment = document.createDocumentFragment();
        rankHeading(fragment, headers, active);
        rankState(fragment, "loading", label);
        target.replaceChildren(fragment);
    }
    target.setAttribute("aria-busy", "true");
}
function failRank(target, headers, active, retry) {
    target.setAttribute("aria-busy", "false");
    if (target.dataset.signature) {
        target.querySelector(".rank-state-error")?.remove();
        rankState(target, "error", "Не вдалося оновити дані.", retry);
    } else {
        const fragment = document.createDocumentFragment();
        rankHeading(fragment, headers, active);
        rankState(fragment, "error", "Не вдалося завантажити результати.", retry);
        target.replaceChildren(fragment);
    }
}
function prepareAccumulatedRank() {
    const mode = $("rankmode").value, metric = $("rankmetric").value, level = $("ranklevel").value;
    const target = $("rankrows"), filter = JSON.stringify([mode, metric, level]);
    const changed = target.dataset.filter !== filter;
    target.dataset.filter = filter;
    prepareRank(target, accumulatedHeaders, activeRankMetric(metric), "Завантаження рейтингу…", changed);
}
function finishRank(target) {
    target.querySelector(".rank-state-error")?.remove();
    target.setAttribute("aria-busy", "false");
}
function rankList(target, headers, players, metrics, selectedMetric = -1) {
    const rows = players.map(player => [player.user, ...metrics(player)]);
    const signature = JSON.stringify([headers, rows, selectedMetric, ownUsername, avatarVersion]);
    if (target.dataset.signature === signature) return;
    const fragment = document.createDocumentFragment();
    rankHeading(fragment, headers, selectedMetric);
    if (!rows.length) rankState(fragment, "empty", "Ще немає результатів.");
    rows.forEach(([user, ...values], i) => {
        const rank = i + 1;
        row(fragment, ["", user, ...values]);
        const line = fragment.lastElementChild;
        line.classList.add("rank-row");
        if (ownUsername && user.toLowerCase() === ownUsername.toLowerCase()) line.classList.add("own-rank");
        const badge = document.createElement("span");
        badge.className = "rank-place " + (rank <= 3 ? "rank-place-" + rank : rank <= 10 ? "rank-place-top" : "");
        badge.textContent = rank;
        badge.title = rank + " місце";
        line.firstElementChild.append(badge);
        profileLink(line.children[1], user);
        if (selectedMetric >= 0) line.children[selectedMetric + 2].classList.add("rank-metric");
    });
    target.replaceChildren(fragment);
    target.dataset.signature = signature;
}
function setTab(t) {
    const changed = tab !== t;
    if (changed) {
        rankingRequest++;
        clearTimeout(rankingFilterTimer);
        rankingFilterTimer = null;
    }
    tab = t;
    saveRoute();
    $("home-brand").classList.toggle("can-return", t !== "menu");
    watchRankings(t === "ranking" || t === "weekly");

    ["menu", "play", "ranking", "weekly", "challenge", "profile", "friends"].forEach(n => {
        $(n).hidden = n !== t;
        if (n === t && changed) animate($(n), "page-enter");
    });
}

async function navigate(t) {
    if (t !== "friends") closePlayerSearch();
    if (guestMode && (t === "challenge" || t === "friends")) return;
    if (t === "friends") {
        try {
            if (!await refreshVerification()) { message("Підтвердьте електронну пошту."); return; }
        } catch (error) { message(error.message); return; }
    }
    if (t !== tab && t !== "play" && !await pauseForNavigation()) return;
    setTab(t);
    if (t === "ranking") {
        clearTimeout(rankingFilterTimer);
        rankingFilterTimer = null;
    }
    const request = ++rankingRequest, session = epoch;
    try {
        if (t === "friends") {
            await loadFriends();
            return
        }
        if (t === "challenge") {
            await loadCalendar();
            return
        }
        if (t === "profile") {
            await loadProfile();
            return
        }
        if (t === "ranking") {
            const mode = $("rankmode").value, metric = $("rankmetric").value, level = $("ranklevel").value;
            const target = $("rankrows");
            prepareAccumulatedRank();
            const ranks = await api("accumulated?all=" + (mode === "all") + "&metric=" + metric + (level ? "&difficulty=" + level : ""));
            if (request !== rankingRequest || session !== epoch || tab !== "ranking") return;
            rankList(target, accumulatedHeaders, ranks,
                r => [r.wins + " / " + r.perfectWins, r.currentStreak + " / " + r.bestStreak, r.score.toLocaleString("uk-UA")], activeRankMetric(metric));
            finishRank(target);
        }
        if (t === "weekly") {
            const target = $("weeklyrows"), old = $("previous-week");
            prepareRank(target, weeklyHeaders, 1, "Завантаження турніру…");
            prepareRank(old, weeklyHeaders, 1, "Завантаження попереднього тижня…");
            const w = await api("weekly");
            if (request !== rankingRequest || session !== epoch || tab !== "weekly") return;
            $("weekly-title").textContent = "Тиждень від " + w.week;
            $("previous-week-title").textContent = w.previousWeek ? "Тиждень від " + w.previousWeek : "Попередній тиждень";
            const metrics = r => [r.games.toLocaleString("uk-UA"), r.score.toLocaleString("uk-UA")];
            rankList(target, weeklyHeaders, w.leaders, metrics, 1);
            rankList(old, weeklyHeaders, w.previousLeaders, metrics, 1);
            finishRank(target);
            finishRank(old);
            await refreshDaily().catch(error => message(error.message));
        }
    }catch (e) {
        if (request !== rankingRequest || session !== epoch || tab !== t) return;
        if (t === "ranking")
            failRank($("rankrows"), accumulatedHeaders, activeRankMetric($("rankmetric").value), () => navigate("ranking"));
        else if (t === "weekly") {
            for (const id of ["weeklyrows", "previous-week"])
                failRank($(id), weeklyHeaders, 1, () => navigate("weekly"));
        } else message(e.message);
    }
}
$("rankmode").onchange = $("rankmetric").onchange = $("ranklevel").onchange = () => {
    if (tab !== "ranking") return;
    ++rankingRequest;
    saveRoute();
    prepareAccumulatedRank();
    clearTimeout(rankingFilterTimer);
    rankingFilterTimer = setTimeout(() => {
        rankingFilterTimer = null;
        if (tab === "ranking") navigate("ranking");
    }, 180);
};

$("home-brand").onclick = () => navigate("menu");
