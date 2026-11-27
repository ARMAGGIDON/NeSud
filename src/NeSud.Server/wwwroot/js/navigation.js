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
let rankingRequest = 0;
function rankList(target, headers, players, metrics, selectedMetric = -1) {
    const rows = players.map(player => [player.user, ...metrics(player)]);
    const signature = JSON.stringify([headers, rows, selectedMetric, ownUsername, avatarVersion]);
    if (target.dataset.signature === signature) return;
    target.dataset.signature = signature;
    const fragment = document.createDocumentFragment();
    row(fragment, ["№", "Гравець", ...headers]);
    fragment.lastElementChild.classList.add("rank-row", "rank-heading");
    if (selectedMetric >= 0) fragment.lastElementChild.children[selectedMetric + 2].classList.add("rank-metric");
    if (!rows.length) emptyList(fragment, "Ще немає результатів.");
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
}
function setTab(t) {
    const changed = tab !== t;
    if (changed) rankingRequest++;
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
            const target = $("rankrows"), filter = JSON.stringify([mode, metric, level]);
            if (target.dataset.filter !== filter) {
                target.replaceChildren();
                delete target.dataset.signature;
                target.dataset.filter = filter;
            }
            if (!target.childElementCount) target.append(loadingIcon("Завантаження рейтингу…"));
            target.setAttribute("aria-busy", "true");
            const ranks = await api("accumulated?all=" + (mode === "all") + "&metric=" + metric + (level ? "&difficulty=" + level : ""));
            if (request !== rankingRequest || session !== epoch || tab !== "ranking") return;
            const active = metric === "score" ? 2 : metric === "wins" || metric === "perfect" ? 0 : 1;
            rankList(target, ["Перемоги / ідеальні", "Серія / рекорд", "Бали"], ranks,
                r => [r.wins + " / " + r.perfectWins, r.currentStreak + " / " + r.bestStreak, r.score.toLocaleString("uk-UA")], active);
            target.setAttribute("aria-busy", "false");
        }
        if (t === "weekly") {
            const target = $("weeklyrows"), old = $("previous-week");
            if (!target.childElementCount) target.append(loadingIcon("Завантаження турніру…"));
            target.setAttribute("aria-busy", "true");
            const w = await api("weekly");
            if (request !== rankingRequest || session !== epoch || tab !== "weekly") return;
            $("weekly-title").textContent = "Тиждень від " + w.week;
            $("previous-week-title").textContent = w.previousWeek ? "Тиждень від " + w.previousWeek : "Попередній тиждень";
            const metrics = r => [r.games.toLocaleString("uk-UA"), r.score.toLocaleString("uk-UA")];
            rankList(target, ["Перемоги", "Бали"], w.leaders, metrics, 1);
            rankList(old, ["Перемоги", "Бали"], w.previousLeaders, metrics, 1);
            target.setAttribute("aria-busy", "false");
            await refreshDaily();
        }
    }catch (e) {
        if (request !== rankingRequest || session !== epoch || tab !== t) return;
        if (t === "ranking" || t === "weekly") {
            const target = $(t === "ranking" ? "rankrows" : "weeklyrows");
            target.setAttribute("aria-busy", "false");
            target.querySelector(".loading-indicator")?.remove();
            target.querySelector(".rank-error")?.remove();
            const error = document.createElement("p");
            error.className = "rank-error empty-list";
            error.textContent = target.childElementCount ? "Не вдалося оновити дані." : "Не вдалося завантажити результати.";
            target.append(error);
            delete target.dataset.signature;
        } else message(e.message);
    }
}
$("rankmode").onchange = $("rankmetric").onchange = $("ranklevel").onchange = () => navigate("ranking");

$("home-brand").onclick = () => navigate("menu");
