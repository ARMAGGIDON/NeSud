"use strict";

let avatarVersion = 0, ownUsername = null;
function playerAvatar(user, known = true) {
    const box = document.createElement("span");
    box.className = "player-avatar";
    setPlayerAvatar(box, user, known);
    return box;
}
function setPlayerAvatar(box, user, known = true) {
    box.replaceChildren();
    box.dataset.user = user || "";
    const fallback = document.createElement("span");
    fallback.className = "avatar-initial";
    fallback.textContent = user ? Array.from(user)[0].toUpperCase() : "?";
    fallback.setAttribute("aria-hidden", "true");
    box.append(fallback);
    if (!user || !known) return;
    const img = document.createElement("img");
    img.alt = "";
    img.loading = box.classList.contains("large") ? "eager" : "lazy";
    img.className = "avatar-pending";
    img.onload = () => { img.classList.remove("avatar-pending"); fallback.hidden = true; };
    img.onerror = () => img.remove();
    img.src = "/api/avatar?user=" + encodeURIComponent(user) +
        (avatarVersion && user === ownUsername ? "&v=" + avatarVersion : "");
    box.append(img);
}
function updateAccountAvatar(settings) {
    const button = $("account");
    ownUsername = !guestMode && settings ? settings.username : null;
    button.replaceChildren();
    if (!guestMode && settings) {
        button.append(playerAvatar(settings.username, settings.hasAvatar), document.createTextNode("Мій профіль"));
    } else button.textContent = guestMode ? "Увійти" : "Мій профіль";
}
function statCards(target, values) {
    target.replaceChildren();
    for (const [label, value] of values) {
        const card = document.createElement("div"), caption = document.createElement("small"), number = document.createElement("strong");
        card.className = "stat-card";
        caption.textContent = label;
        number.textContent = typeof value === "number" ? value.toLocaleString("uk-UA") : value;
        card.append(caption, number);
        target.append(card);
    }
}
function profileLink(el, user) {
    const b = document.createElement("button");
    b.className = "profile-link";
    b.append(playerAvatar(user), document.createTextNode(el.textContent));
    b.onclick = () => openProfile(user);
    el.replaceChildren(b)
}
async function openProfile(user = null) {
    profileUser = !guestMode && ownUsername && user?.toLowerCase() === ownUsername.toLowerCase() ? null : user;
    profilePage = 0;
    $("profile-active").hidden = true;
    $("profile-active-rows").replaceChildren();
    $("profile-level").value = "";
    await navigate("profile")
}
let profileLoadVersion = 0;
async function loadProfile() {
    const user = profileUser, page = profilePage, e = epoch, version = ++profileLoadVersion;
    const level = $("profile-level").value, own = !guestMode && !user;
    const current = () => tab === "profile" && profileUser === user && profilePage === page &&
        epoch === e && $("profile-level").value === level && version === profileLoadVersion;
    const p = await api("profile?page=" + page + (user ? "&user=" + encodeURIComponent(user): "") + (level ? "&difficulty=" + level: ""));
    if (!current()) return;
    let details = null, detailError = null, friends = null;
    if (own) {
        try { details = await api("profile/own"); }
        catch (error) { detailError = error.message; }
    } else if (!guestMode && user && p.verified && verifiedAccount && p.user !== ownUsername) {
        try { friends = await api("friends"); }
        catch (error) { message(error.message); }
    }
    if (!current()) return;
    // Update the visible sections together, rather than shrinking the page between awaits.
    $("profile-name").textContent = p.user;
    $("profile-verified").hidden = !p.verified;
    setPlayerAvatar($("profile-avatar"), p.user, !!p.avatar);
    $("profile-settings").hidden = !own;
    $("profile-settings-open").textContent = p.verified ? "Налаштування профілю" : "Підтвердити пошту";
    $("avatar-edit").hidden = !own || !p.verified;
    $("invite-open").hidden = !own || !p.verified;
    if (own) verifiedAccount = p.verified;
    const metrics = [["Ігор", p.games], ["Перемог", p.wins], ["Ідеальних", p.perfectWins],
        ["Балів", p.score], ["Серія / рекорд", p.currentStreak + " / " + p.bestStreak], ["Щоденних перемог", p.dailyWins]];
    $("profile-active").hidden = !own;
    if (details) {
        metrics.push(["Час", fmt(details.seconds)], ["Помилок", details.mistakes], ["Підказок", details.hints]);
        renderActiveGames(details.active);
    } else {
        $("profile-active-rows").replaceChildren();
        if (detailError) emptyList($("profile-active-rows"), detailError);
    }
    statCards($("profile-stats"), metrics);
    const stats = $("profile-level-stats");
    stats.replaceChildren();
    row(stats, ["Складність", "Перемоги / ігри", "Ідеальні", "Серія / рекорд", "Бали", "Найкращий час"]);
    for (const s of p.byDifficulty || []) row(stats, [levels[s.difficulty], s.wins + " / " + s.games, s.perfectWins, s.currentStreak + " / " + s.bestStreak, s.score, s.bestSeconds == null ? "Немає" : fmt(s.bestSeconds)]);
    $("profile-friend").replaceChildren();
    if (friends) friendActions($("profile-friend"), user, friends.find(f => f.user === user)?.status, loadProfile);
    drawCups($("profile-cups"), p.cups);
    const target = $("profile-history");
    target.replaceChildren();
    row(target, ["Дата / режим", "Складність", "Результат / час", "Бали"]);
    p.history.forEach(r => row(target, [[new Date(r.ended).toLocaleString("uk-UA"), r.challengeDay ? "Щоденна " + r.challengeDay : r.source], levels[r.difficulty], [({ completed: "Перемога", defeated: "Поразка", abandoned: "Перервано" })[r.outcome], fmt(r.seconds)], [r.score + " балів", "Помилки: " + r.mistakes, "Підказки: " + r.hints]]));
    $("profile-prev").disabled = page === 0;
    $("profile-next").disabled = !p.hasMore;
    if (!p.history.length) emptyList(target, "Ігор ще немає.");
}
function renderActiveGames(games) {
    const target = $("profile-active-rows");
    target.replaceChildren();
    if (!games.length) { emptyList(target, "Активних ігор немає."); return; }
    row(target, ["Режим / оновлено", "Складність", "Стан / час", "Дія"]);
    for (const g of games) {
        const mode = g.challengeDay ? "Щоденна " + g.challengeDay : g.source === "desktop" ? "Desktop" : "Web";
        row(target, [[mode, new Date(g.updated).toLocaleString("uk-UA")], levels[g.difficulty],
            [g.paused ? "На паузі" : "Розпочато", fmt(g.seconds)], ""]);
        const cell = target.lastElementChild.lastElementChild;
        if (g.source === "desktop") cell.textContent = "У застосунку";
        else {
            const button = document.createElement("button");
            button.textContent = "Продовжити";
            button.onclick = () => g.challengeDay ? openChallenge(g.challengeDay, true) : openRegular();
            cell.append(button);
        }
    }
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
