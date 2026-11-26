"use strict";

let realtime = null, realtimeToken = "", connecting = null, subscribed = false;
let refreshTimer = null, accountTimer = null, rankingTimer = null;
const pendingDays = new Set();

function stopRealtime() {
    const old = realtime;
    realtime = null;
    realtimeToken = "";
    connecting = null;
    subscribed = false;
    pendingDays.clear();
    clearTimeout(refreshTimer);
    clearTimeout(accountTimer);
    clearTimeout(rankingTimer);
    refreshTimer = accountTimer = rankingTimer = null;
    if (old) old.stop().catch(() => {});
}

function unauthorizedRealtime() {
    token = "";
    sessionStorage.removeItem("token");
    sessionStorage.removeItem("guest");
    resetSession();
    return Object.assign(Error("Сеанс завершено."), { status: 401 });
}

async function subscribeConnection(connection, secret) {
    const accepted = await connection.invoke("Subscribe", secret);
    if (connection !== realtime || secret !== token) throw Error("Акаунт змінено.");
    if (!accepted) throw unauthorizedRealtime();
    subscribed = true;
    await connection.invoke("WatchRankings", tab === "ranking" || tab === "weekly");
}

async function ensureRealtime() {
    if (!token) throw Object.assign(Error("Потрібна авторизація."), { status: 401 });
    if (!globalThis.signalR?.protocols?.msgpack?.MessagePackHubProtocol)
        throw Error("Не завантажено SignalR MessagePack. Виконайте npm run prepare-web.");
    if (realtimeToken !== token) stopRealtime();
    if (connecting) return connecting;
    if (subscribed && realtime?.state === signalR.HubConnectionState.Connected) return realtime;
    if (realtime?.state === signalR.HubConnectionState.Reconnecting ||
        realtime?.state === signalR.HubConnectionState.Connected)
        throw Error("Відновлення WebSocket.");

    const secret = token;
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/game", { transport: signalR.HttpTransportType.WebSockets, skipNegotiation: true })
        .withHubProtocol(new signalR.protocols.msgpack.MessagePackHubProtocol())
        .withAutomaticReconnect([0, 1000, 3000, 10000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();
    realtime = connection;
    realtimeToken = secret;
    connection.on("GameChanged", day => {
        if (connection === realtime && subscribed) queueGameRefresh(day);
    });
    connection.on("AccountChanged", () => {
        if (connection === realtime && subscribed) queueAccountRefresh();
    });
    connection.on("RankingsChanged", () => {
        if (connection === realtime && subscribed) queueRankingRefresh();
    });
    connection.onreconnecting(() => {
        if (connection !== realtime) return;
        subscribed = false;
        disconnect(Error("WebSocket перервано"));
    });
    connection.onreconnected(async () => {
        if (connection !== realtime) return;
        try {
            await subscribeConnection(connection, secret);
            if (connection !== realtime) return;
            // Recover authoritatively; never replay an uncertain write.
            await reconnect();
            queueAccountRefresh();
            queueRankingRefresh();
        } catch (error) {
            if (connection === realtime) { stopRealtime(); disconnect(error); }
        }
    });
    connection.onclose(() => {
        if (connection !== realtime) return;
        stopRealtime();
        disconnect(Error("WebSocket закрито"));
    });
    const task = (async () => {
        try {
            await connection.start();
            await subscribeConnection(connection, secret);
            return connection;
        } catch (error) {
            if (connection === realtime) stopRealtime();
            throw error;
        }
    })();
    connecting = task;
    try { return await task; }
    finally { if (connecting === task) connecting = null; }
}

async function invokeGame(method, ...args) {
    const connection = await ensureRealtime();
    const response = await connection.invoke(method, ...args);
    if (connection !== realtime) throw Error("Акаунт змінено.");
    if (response.status === 401) throw unauthorizedRealtime();
    if (response.status !== 200)
        throw Object.assign(Error(response.data?.error || response.data?.detail ||
            (response.status === 409 ? "Стан гри змінено в іншому клієнті." : "Операція недоступна.")),
            { status: response.status });
    return response.data;
}

function mergeGameDelta(current, data) {
    if (!data?.delta) return data;
    if (!current || current.id !== data.id || current.revision !== data.baseRevision)
        throw Object.assign(Error("Пропущено ревізію. Потрібен повний стан."), { status: 409 });
    return { ...current, ...data.changes, revision: data.revision };
}

const gameSnapshot = (day, pause = false) => invokeGame("Snapshot", day ?? null, pause);
const gameNew = request => invokeGame("New", request.difficulty, request.id, request.revision,
    request.autoFinish, request.challengeDay ?? null);
async function gameMove(request) {
    const data = await invokeGame("Move", request.id, request.revision, request.action,
        request.cell ?? 0, request.digit ?? 0, request.note ?? false, request.reward ?? null,
        request.challengeDay ?? null);
    return mergeGameDelta(game, data);
}

function queueGameRefresh(day) {
    pendingDays.add(day ?? null);
    if (refreshTimer === null) refreshTimer = setTimeout(flushGameRefresh, 100);
}
async function flushGameRefresh() {
    refreshTimer = null;
    if (!pendingDays.size || !token) return;
    if (busy || pulse || authBusy || adBusy || !subscribed) {
        refreshTimer = setTimeout(flushGameRefresh, 250);
        return;
    }
    const currentDay = challengeDay, e = epoch, secret = token;
    const days = [...pendingDays];
    pendingDays.clear();
    busy = true;
    try {
        for (const day of days) {
            if (day !== currentDay && day !== null) continue;
            const data = await gameSnapshot(day, false);
            if (epoch !== e || token !== secret || challengeDay !== currentDay) return;
            if (day === currentDay) {
                const wasTerminal = game?.completed || game?.finalDefeat;
                accept(data);
                if (!wasTerminal && (game?.completed || game?.finalDefeat)) queueAccountRefresh();
            } else {
                regularGame = data?.empty ? null : data;
                render();
            }
        }
        if (tab === "challenge") queueAccountRefresh();
    } catch (error) {
        if (epoch === e && error.status !== 400) disconnect(error);
    } finally {
        busy = false;
        render();
    }
}

function queueAccountRefresh() {
    if (guestMode || accountTimer !== null) return;
    const secret = token;
    accountTimer = setTimeout(async () => {
        accountTimer = null;
        if (secret !== token || guestMode) return;
        if (busy || authBusy || dailyBusy || adBusy) { queueAccountRefresh(); return; }
        await refreshDaily();
        try {
            if (tab === "friends") await loadFriends();
            if (tab === "challenge") await loadCalendar();
            await refreshVerification();
            render();
        } catch {}
    }, 350);
}

function watchRankings(watch) {
    if (subscribed && realtime?.state === signalR.HubConnectionState.Connected)
        realtime.invoke("WatchRankings", watch).catch(() => {});
}
function queueRankingRefresh() {
    if (rankingTimer !== null || (tab !== "ranking" && tab !== "weekly")) return;
    rankingTimer = setTimeout(() => {
        rankingTimer = null;
        if (tab === "ranking" || tab === "weekly") navigate(tab);
    }, 1000);
}
