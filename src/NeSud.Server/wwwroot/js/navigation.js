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
function setTab(t) {
    const changed = tab !== t;
    tab = t;
    $("home-brand").classList.toggle("can-return", t !== "menu");
    watchRankings(t === "ranking" || t === "weekly");

    ["menu", "play", "ranking", "weekly", "challenge", "profile", "friends"].forEach(n => {
        $(n).hidden = n !== t;
        if (n === t && changed) animate($(n), "page-enter");
    });
}

async function navigate(t) {
    if (guestMode && (t === "challenge" || t === "friends")) return;
    if (t === "friends") {
        try {
            if (!await refreshVerification()) { message("Підтвердьте електронну пошту."); return; }
        } catch (error) { message(error.message); return; }
    }
    if (t !== tab && t !== "play" && !await pauseForNavigation()) return;
    setTab(t);
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
            let ranks = await api("accumulated?all=" + (mode === "all") + "&metric=" + metric + (level ? "&difficulty=" + level: "")), target = $("rankrows");
            if (tab !== "ranking" || $("rankmode").value !== mode || $("rankmetric").value !== metric || $("ranklevel").value !== level) return;
            target.replaceChildren();
            row(target, ["Місце / гравець", "Перемоги / ідеальні", "Серія / рекорд", "Бали"]);
            if (!ranks.length) emptyList(target, "Ще немає результатів.");
            ranks.forEach((r, i) => {
                row(target, [(i + 1) + ". " + r.user, r.wins + " / " + r.perfectWins, r.currentStreak + " / " + r.bestStreak, r.score]);
                profileLink(target.lastElementChild.firstElementChild, r.user)
            });
        }
        if (t === "weekly") {
            const w = await api("weekly");
            if (tab !== "weekly") return;
            $("weekly-title").textContent = "Тиждень від " + w.week;
            const target = $("weeklyrows");
            target.replaceChildren();
            row(target, ["Місце", "Гравець", "Перемоги", "Бали"]);
            if (!w.leaders.length) emptyList(target, "Ще немає результатів.");
            w.leaders.forEach((r, i) => {
                row(target, [i + 1, r.user, r.games, r.score]);
                profileLink(target.lastElementChild.children[1], r.user)
            });
            const old = $("previous-week");
            old.replaceChildren();
            row(old, ["Місце", "Гравець", "Перемоги", "Бали"]);
            if (!w.previousLeaders.length) emptyList(old, "Немає результатів попереднього тижня.");
            w.previousLeaders.forEach((r, i) => {
                row(old, [i + 1, r.user, r.games, r.score]);
                profileLink(old.lastElementChild.children[1], r.user)
            });
            await refreshDaily();
        }
    }catch (e) {
        message(e.message)
    }
}
$("rankmode").onchange = $("rankmetric").onchange = $("ranklevel").onchange = () => navigate("ranking");

$("home-brand").onclick = () => navigate("menu");
