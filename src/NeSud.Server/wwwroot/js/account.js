"use strict";

function resetSession() {
    regularGame = null;
    challengeDay = null;
    calendarMonth = null;
    daily = null;
    revealed = false;
    if ($("daily").open) $("daily").close();
    epoch++;
    game = null;
    online = false;
    guestMode = true;
    verifiedAccount = false;
    selected = 0;
    note = false;
    adPending = false;
    if ($("ad").open) $("ad").close();
    $("account").hidden = false;
    $("account").textContent = "Увійти";
    $("logout").hidden = true;
    $("history-tab").disabled = true;
    $("guest-notice").hidden = false;
    setTab("menu");
    render();
}
async function pauseForNavigation() {
    if (pulse) await pulse;
    if (busy) return false;
    if (game && !game.paused && !game.completed && !game.outOfLives && online) return await act("pause");
    return true
}
async function showAuth() {
    if (!await pauseForNavigation()) return;
    mode(false);
    authError("");
    if (!$("auth").open) $("auth").showModal()
}
$("account").onclick = () => guestMode ? showAuth() : openProfile();
$("auth").addEventListener("close", () => {
    $("password").value = $("confirmation").value = ""
});
function authError(text) {
    $("auth-error").textContent = text;
    $("auth-error").hidden = !text
}
function mode(register) {
    registration = register;
    $("user-label").textContent = register ? "Нікнейм" : "Логін або email";
    $("register-email-wrap").hidden = $("register-invite-wrap").hidden = !register;
    $("register-email").required = register;
    $("auth-title").textContent = register ? "Створення акаунта": "Вхід до акаунта";
    $("confirmation-wrap").hidden = !register;
    $("confirmation").required = register;
    $("confirmation").value = "";
    $("password").autocomplete = register ? "new-password": "current-password";
    $("submit-auth").textContent = register ? "Зареєструватися": "Увійти";
    $("register-tab").classList.toggle("active", register);
    $("login-tab").classList.toggle("active", !register);
    authError("")
}
$("login-tab").onclick = () => mode(false);
$("register-tab").onclick = () => mode(true);
document.querySelectorAll("[data-secret]").forEach(b => b.onclick = () => {
    const input = $(b.dataset.secret), show = input.type === "password";
    input.type = show ? "text": "password";
    b.textContent = show ? "Приховати": "Показати";
    b.setAttribute("aria-pressed", String(show));
    b.setAttribute("aria-label", b.textContent + " пароль")
});
async function login(guest = false) {
    if (authBusy || busy) return;
    authError("");
    if (guest) nextGuest = Date.now() + 15000;
    if (!guest && registration && $("password").value !== $("confirmation").value) {
        authError("Паролі не збігаються.");
        $("confirmation").focus();
        return
    }
    authBusy = true;
    $("submit-auth").disabled = $("guest").disabled = $("login-tab").disabled = $("register-tab").disabled = true;
    try {
        if (guest && token && guestMode) {
            if ($("auth").open) $("auth").close();
            return
        }
        const oldToken = token, oldGuest = guestMode;
        const a = await api(guest ? "guest": registration ? "register": "login", guest ? { }: { user: $("user").value, password: $("password").value, confirmation: registration ? $("confirmation").value : null, email: registration ? $("register-email").value : null, invite: registration ? $("register-invite").value : null });
        regularGame = null;
        challengeDay = null;
        calendarMonth = null;
        daily = null;
        epoch++;
        game = null;
        selected = 0;
        note = false;
        token = a.token;
        guestMode = guest;
        verifiedAccount = false;
        sessionStorage.setItem("token", token);
        sessionStorage.setItem("guest", guest ? "1": "0");
        $("password").value = $("confirmation").value = "";
        if ($("auth").open) $("auth").close();
        if (oldGuest && oldToken && oldToken !== token) fetch("/api/logout", { method: "POST", headers: { Authorization: "Bearer " + oldToken } }).catch (() => { });
        await start();
        if (!guest && registration) {
            history.replaceState(null, "", location.pathname);
            await openProfile();
            try { await api("account/verification/send", {}); profileNotice("Код надіслано на пошту."); }
            catch (error) { profileNotice("Акаунт створено. " + error.message); }
        }
    }
    catch (e) {
        const error = e.name === "AbortError" || e instanceof TypeError ? "Не вдалося з’єднатися з сервісом. Спробуйте ще раз.": e.message;
        if (guest) message(error);
        else authError(error)
    }
    finally {
        authBusy = false;
        $("submit-auth").disabled = $("guest").disabled = $("login-tab").disabled = $("register-tab").disabled = false
    }
}
$("login").onsubmit = e => {
    e.preventDefault();
    login()
};
$("guest").onclick = () => login(true);
$("logout").onclick = async() => {
    if (busy || authBusy) return;
    try {
        if (game && !game.paused && !game.completed) await act("pause");
        await api("logout", { })
    }catch { }token = "";
    sessionStorage.removeItem("token");
    sessionStorage.removeItem("guest");
    resetSession();
    message("");
    await login(true)
};
async function start() {
    $("app").hidden = false;
    $("account").hidden = false;
    $("account").textContent = guestMode ? "Увійти" : "Мій профіль";
    $("logout").hidden = guestMode;
    $("guest-notice").hidden = !guestMode;
    $("history-tab").disabled = guestMode;
    await navigate("menu");
    await reconnect();
    if (!guestMode) {
        try { await refreshVerification(); } catch { verifiedAccount = false; }
        refreshDaily();
    }
    render();
}

async function refreshVerification() {
    if (guestMode || !token) { verifiedAccount = false; return false; }
    const currentEpoch = epoch;
    const settings = await api("account/settings");
    if (epoch !== currentEpoch || guestMode) return false;
    verifiedAccount = settings.verified === true;
    return verifiedAccount;
}
window.addEventListener("focus", async () => {
    if (guestMode || !token) return;
    try { await refreshVerification(); render(); } catch { }
});
