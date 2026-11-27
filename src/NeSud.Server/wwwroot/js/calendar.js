"use strict";

async function loadCalendar() {
    $("calendar-login").hidden = !guestMode;
    $("calendar-content").hidden = guestMode;
    $("calendar-prev").hidden = $("calendar-next").hidden = guestMode;
    if (guestMode) {
        $("calendar-days").replaceChildren();
        $("calendar-cups").replaceChildren();
        $("calendar-progress").textContent = "";
        $("calendar-prev").disabled = $("calendar-next").disabled = true;
        $("calendar-title").textContent = "Щоденні ігри доступні в акаунті";
        return
    }
    const e = epoch, c = await api("challenge/calendar" + (calendarMonth ? "?month=" + encodeURIComponent(calendarMonth): ""));
    if (tab !== "challenge" || epoch !== e) return;
    calendarMonth = c.month;
    $("calendar-prev").disabled = c.month === "2000-01";
    $("calendar-title").textContent = new Date(c.month + "-01T12:00:00").toLocaleDateString("uk-UA", { month: "long", year: "numeric" });
    $("calendar-next").disabled = c.month === c.today.slice(0, 7);
    $("calendar-progress").textContent = c.days.filter(d => d.status === "won").length + " / " + c.days.length;
    const grid = $("calendar-days");
    grid.replaceChildren();
    for (let i = 0; i < (new Date(c.month + "-01T12:00:00").getDay() + 6) % 7; i++) grid.append(document.createElement("span"));
    for (const d of c.days) {
        const b = document.createElement("button");
        b.className = "calendar-day " + d.status;
        b.textContent = Number(d.day.slice(-2)) + (d.status === "won" ? " ✓": "");
        b.title = d.day + ": " + ({ won: "Перемога", future: "Майбутній день", missed: "Пропущено", available: "Почати", started: "Продовжити", retry: "Повторити" })[d.status];
        b.disabled = ["future", "missed", "won"].includes(d.status);
        b.onclick = () => openChallenge(d.day);
        grid.append(b)
    }
    drawCups($("calendar-cups"), c.cups);
    await refreshDaily();
}
function changeMonth(delta) {
    if (!calendarMonth) return;
    const [y, m] = calendarMonth.split("-").map(Number);
    const d = new Date(Date.UTC(y, m - 1 + delta, 1));
    calendarMonth = d.toISOString().slice(0, 7);
    navigate("challenge")
}
async function openChallenge(day) {
    if (guestMode) return;
    if (!await pauseForNavigation()) return;
    epoch++;
    challengeDay = day;
    game = null;
    selected = 0;
    note = false;
    comparison = null;
    await navigate("play");
    await reconnect();
    if (online && !game && challengeDay === day) await $("newgame").onclick()
}
async function openRegular() {
    if (challengeDay) {
        if (!await pauseForNavigation()) return;
        epoch++;
        challengeDay = null;
        game = null;
        selected = 0;
        note = false;
        comparison = null;
        await navigate("play");
        await reconnect()
    } else {
        await navigate("play");
        await reconnect();
    }
}
$("calendar-prev").onclick = () => changeMonth(-1);
$("calendar-next").onclick = () => changeMonth(1);
$("challenge-rewards").onclick = () => {
    dailyMode = "challenge";
    $("daily-open").onclick()
};
$("profile-level").onchange = () => {
    profilePage = 0;
    loadProfile().catch (e => message(e.message))
};
$("profile-prev").onclick = () => {
    profilePage = Math.max(0, profilePage - 1);
    navigate("profile")
};
$("profile-next").onclick = () => {
    profilePage++;
    navigate("profile")
};
$("calendar-login").onclick = showAuth;
