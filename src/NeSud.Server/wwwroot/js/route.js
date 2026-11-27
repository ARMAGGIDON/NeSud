"use strict";

// Navigation belongs to this browser tab, not the account or the game database.
const routeStorageKey = "nesud.navigation.v1";
const routeTabs = ["menu", "play", "ranking", "weekly", "challenge", "profile", "friends"];
function normalizeRoute(value, guest) {
    const input = value && typeof value === "object" ? value : {};
    const choice = (value, allowed, fallback) => allowed.includes(value) ? value : fallback;
    const level = value => choice(value, ["", "Easy", "Medium", "Hard", "Expert"], "");
    const date = (value, month = false) => {
        if (typeof value !== "string" || !(month ? /^\d{4}-\d{2}$/ : /^\d{4}-\d{2}-\d{2}$/).test(value)) return null;
        const full = month ? value + "-01" : value;
        const d = new Date(full + "T00:00:00Z");
        return Number.isFinite(d.getTime()) && d.toISOString().slice(0, 10) === full ? value : null;
    };
    let page = choice(input.tab, routeTabs, "menu");
    if (guest && (page === "challenge" || page === "friends" || page === "play" && input.challengeDay)) page = "menu";
    return {
        tab: page,
        challengeDay: page === "play" && !guest ? date(input.challengeDay) : null,
        calendarMonth: page === "challenge" ? date(input.calendarMonth, true) : null,
        profileUser: page === "profile" && typeof input.profileUser === "string" && input.profileUser.trim()
            && input.profileUser.length <= 80 ? input.profileUser : null,
        profilePage: page === "profile" && Number.isInteger(input.profilePage)
            && input.profilePage >= 0 && input.profilePage <= 2147483647 ? input.profilePage : 0,
        profileLevel: level(input.profileLevel),
        rankMode: choice(input.rankMode, ["server", "all"], "server"),
        rankMetric: choice(input.rankMetric, ["score", "wins", "perfect", "current", "streak"], "score"),
        rankLevel: level(input.rankLevel)
    };
}
function readRoute() {
    try { return normalizeRoute(JSON.parse(sessionStorage.getItem(routeStorageKey)), guestMode); }
    catch { return normalizeRoute(null, guestMode); }
}
function clearRoute() {
    try { sessionStorage.removeItem(routeStorageKey); } catch { }
}
function saveRoute() {
    if (!token) return;
    const route = normalizeRoute({
        tab, challengeDay, calendarMonth, profileUser, profilePage,
        profileLevel: $("profile-level").value,
        rankMode: $("rankmode").value, rankMetric: $("rankmetric").value, rankLevel: $("ranklevel").value
    }, guestMode);
    try { sessionStorage.setItem(routeStorageKey, JSON.stringify(route)); } catch { }
}
function applyRoute(route) {
    challengeDay = route.challengeDay;
    calendarMonth = route.calendarMonth;
    profileUser = route.profileUser;
    profilePage = route.profilePage;
    $("profile-level").value = route.profileLevel;
    $("rankmode").value = route.rankMode;
    $("rankmetric").value = route.rankMetric;
    $("ranklevel").value = route.rankLevel;
}
