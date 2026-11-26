"use strict";

function row(parent, values) {
    let r = document.createElement("div");
    r.className = "record";
    values.forEach(v => {
        let s = document.createElement("span");
        s.textContent = v;
        r.append(s)
    });
    parent.append(r)
}
function setTab(t) {
    tab = t;
    $("back-menu").hidden = t === "menu";
    $("help-open").hidden = t === "menu";
    ["menu", "play", "history", "ranking", "weekly", "challenge", "profile", "friends"].forEach(n => {
        $(n).hidden = n !== t;
    });
}

async function navigate(t) {
    if (guestMode && (t === "challenge" || t === "history")) return;
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
        if (t === "history" && !guestMode) {
            let records = await api("history"), drafts = await api("desktop/drafts"), target = $("historyrows"), wins = records.filter(r => r.outcome === "completed");
            if (tab !== "history") return;
            target.replaceChildren();
            $("summary").textContent = "Ігор: " + records.length + " · Перемог: " + wins.length + " · Балів: " + wins.reduce((s, r) => s + r.score, 0) + " · Час: " + fmt(records.reduce((s, r) => s + r.game.seconds, 0));
            let streak = 0, best = 0, perfect = 0;
            for (const r of [...records].reverse()) {
                if (r.outcome === "completed") {
                    streak++;
                    best = Math.max(best, streak);
                    if (!r.game.mistakes && !r.game.hints && !r.game.candidateUses && !r.game.autoFilled && !r.game.lifeRestores) perfect++
                }else streak = 0
            }$("summary").textContent += " · Ідеальні: " + perfect + " · Серія: " + streak + " · Рекорд серії: " + best;
            for (let d = 0; d < 4; d++) {
                const group = records.filter(r => r.game.puzzle.difficulty === d), done = group.filter(r => r.outcome === "completed");
                let current = 0, best = 0;
                for (const r of [...group].reverse()) {
                    current = r.outcome === "completed" ? current + 1: 0;
                    best = Math.max(best, current)
                }
                const perfect = done.filter(r => !r.game.mistakes && !r.game.hints && !r.game.candidateUses && !r.game.autoFilled && !r.game.lifeRestores).length;
                row(target, [levels[d], done.length + "/" + group.length, "Ідеальні: " + perfect + " · Серія: " + current + "/" + best, done.reduce((s, r) => s + r.score, 0) + " балів"])
            }row(target, ["Дата / джерело", "Складність", "Результат / час", "Оцінка"]);
            if (!challengeDay && game && !game.completed && !game.finalDefeat) row(target, ["Поточна · web", levels[game.difficulty], "У процесі · " + fmt(game.seconds), "–"]);
            drafts.forEach(r => row(target, [new Date(r.updated).toLocaleString("uk-UA") + " · desktop", levels[r.game.puzzle.difficulty], "У процесі · " + fmt(r.game.seconds), "–"]));
            records.forEach(r => row(target, [new Date(r.ended).toLocaleString("uk-UA") + " · " + (r.challengeDay ? "Щоденна " + r.challengeDay : r.source), levels[r.game.puzzle.difficulty], (r.outcome === "completed" ? "Перемога": r.outcome === "defeated" ? "Поразка": "Перервано") + " · " + fmt(r.game.seconds), r.score + " балів · помилки: " + r.game.mistakes + " · підказки: " + r.game.hints + " · кандидати: " + (r.game.candidateUses || 0) + " · відновлення: " + (r.game.lifeRestores || 0)]));
        }
        if (t === "ranking") {
            const mode = $("rankmode").value, metric = $("rankmetric").value, level = $("ranklevel").value;
            let ranks = await api("accumulated?all=" + (mode === "all") + "&metric=" + metric + (level ? "&difficulty=" + level: "")), target = $("rankrows");
            if (tab !== "ranking" || $("rankmode").value !== mode || $("rankmetric").value !== metric || $("ranklevel").value !== level) return;
            target.replaceChildren();
            row(target, ["Місце / гравець", "Перемоги / ідеальні", "Серія / рекорд", "Бали"]);
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
            w.leaders.forEach((r, i) => {
                row(target, [i + 1, r.user, r.games, r.score]);
                profileLink(target.lastElementChild.children[1], r.user)
            });
            const old = $("previous-week");
            old.replaceChildren();
            row(old, ["Місце", "Гравець", "Перемоги", "Бали"]);
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
