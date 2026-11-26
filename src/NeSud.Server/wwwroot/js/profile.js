"use strict";

function profileLink(el, user) {
    const b = document.createElement("button");
    b.className = "profile-link";
    b.textContent = el.textContent;
    b.onclick = () => openProfile(user);
    el.replaceChildren(b)
}
async function openProfile(user = null) {
    profileUser = user;
    profilePage = 0;
    $("profile-level").value = "";
    await navigate("profile")
}
async function loadProfile() {
    const user = profileUser, page = profilePage, e = epoch;
    const level = $("profile-level").value;
    const p = await api("profile?page=" + page + (user ? "&user=" + encodeURIComponent(user): "") + (level ? "&difficulty=" + level: ""));
    if (tab !== "profile" || profileUser !== user || profilePage !== page || epoch !== e || $("profile-level").value !== level) return;
    $("profile-name").textContent = p.user;
    $("profile-verified").hidden = !p.verified;
    $("profile-avatar").hidden = !p.avatar;
    if (p.avatar) $("profile-avatar").src = p.avatar + "&v=" + Date.now();
    else $("profile-avatar").removeAttribute("src");
    $("profile-settings").hidden = guestMode || !!user;
    if (!guestMode && !user) await loadProfileSettings();
    $("profile-stats").textContent = "Ігор: " + p.games + " · Перемог: " + p.wins + " · Ідеальних: " + p.perfectWins + " · Серія: " + p.currentStreak + " / " + p.bestStreak + " · Балів: " + p.score + " · Щоденних перемог: " + p.dailyWins;
    const stats = $("profile-level-stats");
    stats.replaceChildren();
    row(stats, ["Складність", "Перемоги / ігри", "Ідеальні", "Серія / рекорд", "Бали"]);
    for (const s of p.byDifficulty || []) row(stats, [levels[s.difficulty], s.wins + " / " + s.games, s.perfectWins, s.currentStreak + " / " + s.bestStreak, s.score]);
    $("profile-friend").replaceChildren();
    if (!guestMode && user && p.verified && await refreshVerification()) {
        if (tab !== "profile" || epoch !== e || profileUser !== user) return;
        const friends = await api("friends");
        if (tab !== "profile" || epoch !== e || profileUser !== user) return;
        friendActions($("profile-friend"), user, friends.find(f => f.user === user)?.status, loadProfile)
    }
    drawCups($("profile-cups"), p.cups);
    const target = $("profile-history");
    target.replaceChildren();
    row(target, ["Дата / режим", "Складність", "Результат / час", "Бали"]);
    p.history.forEach(r => row(target, [new Date(r.ended).toLocaleString("uk-UA") + " · " + (r.challengeDay ? "Щоденна " + r.challengeDay: r.source), levels[r.difficulty], ({ completed: "Перемога", defeated: "Поразка", abandoned: "Перервано" })[r.outcome] + " · " + fmt(r.seconds), r.score + " · помилки: " + r.mistakes + " · підказки: " + r.hints]));
    $("profile-prev").disabled = page === 0;
    $("profile-next").disabled = !p.hasMore;
}
function drawCups(target, cups) {
    target.replaceChildren();
    if (!cups.length) {
        target.textContent = "Кубків ще немає.";
        return
    }
    for (const month of cups) {
        const el = document.createElement("div");
        el.className = "month-cup";
        const cup = document.createElement("span");
        cup.textContent = "🏆";
        const title = document.createElement("span");
        title.textContent = month;
        el.append(cup, title);
        target.append(el)
    }
}
