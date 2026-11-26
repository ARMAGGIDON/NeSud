"use strict";

$("menu-continue").onclick = () => openRegular();
document.querySelectorAll("[data-new-level]").forEach(button => {
    button.onclick = async () => {
        await openRegular();
        if (tab !== "play" || challengeDay) return;
        $("difficulty").value = button.dataset.newLevel;
        await $("newgame").onclick();
    };
});


document.querySelectorAll("[data-tab]").forEach(b => b.onclick = () => b.dataset.tab === "play" ? openRegular(): navigate(b.dataset.tab));
const inviteCode = new URLSearchParams(location.search).get("invite");
if (inviteCode) $("register-invite").value = inviteCode;
render();
if (token) start();
else {
    resetSession();
    login(true)
}
