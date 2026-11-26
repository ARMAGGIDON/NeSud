"use strict";

$("menu-continue").onclick = () => openRegular();
$("menu-start").onclick = async() => {
    await openRegular();
    $("difficulty").value = $("menu-level").value;
    await $("newgame").onclick()
};
$("menu-profile").onclick = () => guestMode ? showAuth(): openProfile();

document.querySelectorAll("[data-tab]").forEach(b => b.onclick = () => b.dataset.tab === "play" ? openRegular(): navigate(b.dataset.tab));
render();
if (token) start();
else {
    resetSession();
    login(true)
}
