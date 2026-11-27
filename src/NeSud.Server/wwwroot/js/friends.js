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
let friendsLoadVersion = 0;
async function loadFriends() {
    const version = ++friendsLoadVersion, e = epoch, q = $("player-search").value.trim();
    const target = $("friend-list"), results = $("player-results");
    const current = () => tab === "friends" && epoch === e && version === friendsLoadVersion;
    if (guestMode || !verifiedAccount) {
        target.textContent = "Підтвердьте електронну пошту, щоб користуватися друзями.";
        results.replaceChildren();
        return;
    }
    $("player-search-status").textContent = q.length >= 2 ? "Пошук…" : "Введіть щонайменше 2 символи.";
    results.replaceChildren();
    const [friends, matches] = await Promise.allSettled([
        api("friends"), q.length >= 2 ? api("players?q=" + encodeURIComponent(q)) : Promise.resolve([])
    ]);
    if (!current()) return;
    if (friends.status === "fulfilled") {
        const fragment = document.createDocumentFragment();
        for (const [status, label] of [["incoming", "Вхідні запити"], ["outgoing", "Надіслані запити"], ["friend", "Друзі"]]) {
            const section = document.createElement("section"), h = document.createElement("h3");
            h.textContent = label;
            section.className = "friends-section";
            section.append(h);
            const entries = friends.value.filter(x => x.status === status);
            for (const f of entries) {
                const r = document.createElement("div"), b = document.createElement("button");
                r.className = "friend-row";
                b.className = "profile-link";
                b.append(playerAvatar(f.user), document.createTextNode(f.user));
                b.onclick = () => openProfile(f.user);
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
    } else message(friends.reason.message);
    if ($("player-search").value.trim() !== q) {
        $("player-search-status").textContent = "Натисніть «Знайти / оновити».";
        return;
    }
    if (q.length < 2) return;
    if (matches.status === "rejected") {
        $("player-search-status").textContent = matches.reason.message;
        return;
    }
    const names = matches.value.filter(name => name.toLowerCase() !== ownUsername?.toLowerCase());
    $("player-search-status").textContent = names.length ? "Знайдено: " + names.length : "Гравців не знайдено.";
    const fragment = document.createDocumentFragment();
    for (const name of names) {
        const b = document.createElement("button");
        b.className = "profile-link";
        b.append(playerAvatar(name), document.createTextNode(name));
        b.onclick = () => openProfile(name);
        fragment.append(b);
    }
    results.replaceChildren(fragment);
}
$("player-search").addEventListener("input", () => {
    $("player-results").replaceChildren();
    $("player-search-status").textContent = $("player-search").value.trim().length >= 2
        ? "Натисніть «Знайти / оновити»." : "Введіть щонайменше 2 символи.";
});
$("player-form").onsubmit = e => {
    e.preventDefault();
    loadFriends().catch(e => message(e.message));
};
