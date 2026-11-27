"use strict";
const $ = id => document.getElementById(id), levels = ["Легка", "Середня", "Складна", "Експерт"];
let daily = null, dailyBusy = false, revealed = false, resultKey = "", comparison = null, reviveEnd = 0, previousGame = null;
let clockAnchor = performance.now(), pulse = null, dailyTask = null, dailyMode = "win";
let regularGame = null;
let verifiedAccount = false;
let challengeDay = null, calendarMonth = null, profileUser = null, profilePage = 0;
let autoFinish = localStorage.getItem("autoFinish") === "1";
let paintMode = localStorage.getItem("paintDigit") === "1";
let highlightPeers = localStorage.getItem("highlightPeers") !== "0", autoClean = localStorage.getItem("autoClean") !== "0";
let adPending = false, adClock = null, adKind = "", adRemaining = 8, adBusy = false, nextGuest = 0;
let token = sessionStorage.getItem("token") || "", guestMode = sessionStorage.getItem("guest") === "1", registration = false, authBusy = false, game = null, selected = 0, note = false, online = false, busy = false, tab = "menu", epoch = 0;
const message = s => $("status").textContent = s;
if (localStorage.getItem("theme") === "light") document.documentElement.classList.add("light");
$("theme").onclick = () => {
    document.documentElement.classList.toggle("light");
    localStorage.setItem("theme", document.documentElement.classList.contains("light") ? "light": "dark")
};
async function api(path, body) {
    const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), path === "web/new" || path === "account/verification/send" ? 30000 : 5000);
    try {
        const r = await fetch("/api/" + path, { method: body === undefined ? "GET": "POST", headers: { "Content-Type": "application/json", "Authorization": "Bearer " + token }, body: body === undefined ? undefined: JSON.stringify(body), signal: controller.signal });
        if (r.status === 401 && path !== "login") {
            token = "";
            sessionStorage.removeItem("token");
            sessionStorage.removeItem("guest");
            resetSession();
            throw Error("Сеанс завершено. Відновлюємо демо.");
        }
        if (!r.ok) {
            const problem = await r.json().catch (() => ({ }));
            throw Object.assign(Error(problem.error || problem.detail || (r.status === 409 ? "Сеанс змінено в іншій вкладці.": r.status === 429 ? "Забагато спроб. Спробуйте через хвилину.": "Сервіс тимчасово недоступний.")), { status: r.status });
        }
        return await r.json().catch (() => null);
    }finally {
        clearTimeout(timeout)
    }
}
