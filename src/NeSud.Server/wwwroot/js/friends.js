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
async function loadFriends() {
    const target = $("friend-list");
    target.replaceChildren();
    if (!guestMode) {
        const list = await api("friends");
        if (tab !== "friends") return;
        for (const [status, label] of [["incoming", "Вхідні запити"], ["outgoing", "Надіслані запити"], ["friend", "Друзі"]]) {
            const h = document.createElement("h3");
            h.textContent = label;
            target.append(h);
            for (const f of list.filter(x => x.status === status)) {
                const r = document.createElement("div");
                r.className = "friend-row";
                const b = document.createElement("button");
                b.textContent = f.user;
                b.className = "profile-link";
                b.onclick = () => openProfile(f.user);
                r.append(b);
                friendActions(r, f.user, f.status, loadFriends);
                target.append(r)
            }
        }
    }else {
        target.textContent = "Для додавання друзів увійдіть до акаунта."
    }
    const q = $("player-search").value.trim(), results = $("player-results");
    results.replaceChildren();
    if (q.length >= 2) {
        for (const name of await api("players?q=" + encodeURIComponent(q))) {
            if (tab !== "friends") return;
            const b = document.createElement("button");
            b.textContent = name;
            b.onclick = () => openProfile(name);
            results.append(b)
        }
    }
}
$("player-form").onsubmit = e => {
    e.preventDefault();
    loadFriends().catch (e => message(e.message))
};
