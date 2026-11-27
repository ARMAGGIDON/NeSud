"use strict";

function friendActions(target, user, status, reload) {
    const actions = status === "incoming" ? [["Прийняти", "accept"], ["Відхилити", "decline"]]: status === "outgoing" ? [["Скасувати", "cancel"]]: status === "friend" ? [["Видалити", "remove"]]: [["Додати в друзі", "send"]];
    for (const [label, action] of actions) {
        const b = document.createElement("button");
        b.textContent = label;
        b.onclick = async() => {
            b.disabled = true;
            try {
                await api("friends", { user, action });
                await reload()
            }catch (e) {
                message(e.message);
                b.disabled = false
            }
        };
        target.append(b)
    }
}
let friendsLoadVersion = 0, playerSearchVersion = 0, playerSearchTimer;
function closePlayerSearch() {
    ++playerSearchVersion;
    clearTimeout(playerSearchTimer);
    $("player-search-popup").hidden = true;
    $("player-search").setAttribute("aria-expanded", "false");
}
function openPlayerSearch() {
    $("player-search-popup").hidden = false;
    $("player-search").setAttribute("aria-expanded", "true");
}
async function searchPlayers() {
    clearTimeout(playerSearchTimer);
    const version = ++playerSearchVersion, e = epoch, q = $("player-search").value.trim();
    const results = $("player-results"), status = $("player-search-status");
    results.replaceChildren();
    if (tab !== "friends" || guestMode || !verifiedAccount || q.length < 2) { closePlayerSearch(); return; }
    openPlayerSearch();
    status.textContent = "Пошук…";
    const current = () => tab === "friends" && epoch === e && version === playerSearchVersion && $("player-search").value.trim() === q;
    try {
        const names = (await api("players?q=" + encodeURIComponent(q))).filter(name => name.toLowerCase() !== ownUsername?.toLowerCase());
        if (!current()) return;
        status.textContent = names.length ? "Знайдено: " + names.length : "Гравців не знайдено.";
        const fragment = document.createDocumentFragment();
        for (const name of names) {
            const b = document.createElement("button");
            b.type = "button";
            b.className = "profile-link";
            b.append(playerAvatar(name), document.createTextNode(name));
            b.onclick = () => { closePlayerSearch(); openProfile(name); };
            fragment.append(b);
        }
        results.replaceChildren(fragment);
    } catch (error) {
        if (current()) status.textContent = error.message;
    }
}
async function loadFriends() {
    const version = ++friendsLoadVersion, e = epoch;
    const target = $("friend-list");
    const current = () => tab === "friends" && epoch === e && version === friendsLoadVersion;
    if (guestMode || !verifiedAccount) {
        target.textContent = "Підтвердьте електронну пошту, щоб користуватися друзями.";
        closePlayerSearch();
        return;
    }
    try {
        const list = await api("friends");
        if (!current()) return;
        const fragment = document.createDocumentFragment();
        for (const [status, label] of [["incoming", "Вхідні запити"], ["outgoing", "Надіслані запити"], ["friend", "Друзі"]]) {
            const section = document.createElement("section"), h = document.createElement("h3");
            h.textContent = label;
            section.className = "friends-section";
            section.append(h);
            const entries = list.filter(x => x.status === status);
            for (const f of entries) {
                const r = document.createElement("div"), b = document.createElement("button");
                r.className = "friend-row";
                b.className = "profile-link";
                b.append(playerAvatar(f.user), document.createTextNode(f.user));
                b.onclick = () => { closePlayerSearch(); openProfile(f.user); };
                r.append(b);
                friendActions(r, f.user, f.status, loadFriends);
                section.append(r);
            }
            if (!entries.length) {
                const empty = document.createElement("small");
                empty.className = "friends-empty";
                empty.textContent = status === "friend" ? "Друзів ще немає." : "Запитів немає.";
                section.append(empty);
            }
            fragment.append(section);
        }
        target.replaceChildren(fragment);
    } catch (error) { if (current()) message(error.message); }
}
$("player-search").addEventListener("input", () => {
    closePlayerSearch();
    if ($("player-search").value.trim().length >= 2)
        playerSearchTimer = setTimeout(searchPlayers, 250);
});
$("player-form").onsubmit = e => {
    e.preventDefault();
    searchPlayers();
    loadFriends();
};
$("player-form").addEventListener("keydown", e => {
    const popup = $("player-search-popup"), input = $("player-search");
    if (e.key === "Escape" && !popup.hidden) {
        e.preventDefault(); e.stopPropagation(); closePlayerSearch(); input.focus(); return;
    }
    if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
    const rows = Array.from($("player-results").children);
    if (popup.hidden) {
        if (e.key === "ArrowDown" && input.value.trim().length >= 2) { e.preventDefault(); searchPlayers(); }
        return;
    }
    if (!rows.length) return;
    e.preventDefault();
    const i = rows.indexOf(document.activeElement);
    if (e.key === "ArrowUp" && i <= 0) input.focus();
    else rows[Math.min(rows.length - 1, Math.max(0, i + (e.key === "ArrowDown" ? 1 : -1)))].focus();
});
document.addEventListener("pointerdown", e => { if (!$("player-form").contains(e.target)) closePlayerSearch(); });
$("player-form").addEventListener("focusout", e => {
    if (e.relatedTarget && !$("player-form").contains(e.relatedTarget)) closePlayerSearch();
});
