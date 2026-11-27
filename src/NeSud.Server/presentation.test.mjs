import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = name => readFileSync(new URL("./wwwroot/js/" + name, import.meta.url), "utf8");

function deferred() {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
}

function comparisonHarness() {
    const requests = [];
    const api = () => {
        const request = deferred();
        requests.push(request);
        return request.promise;
    };
    const elements = new Map();
    const $ = id => {
        if (!elements.has(id)) elements.set(id, {});
        return elements.get(id);
    };
    const app = new Function("$", "api", `
        let game = { id: "a", completed: true, difficulty: 1, currentScore: 100, seconds: 60, rulesVersion: 3 };
        let epoch = 0, resultKey = "", comparison = null, autoFinish = false, renders = 0;
        const render = () => renders++;
        ${read("results.js")}
        return { loadComparison, get comparison() { return comparison; }, get renders() { return renders; },
            nextGame() { epoch++; game = { ...game, id: "b" }; resultKey = ""; comparison = null; },
            retry() { resultKey = ""; return loadComparison(); } };
    `)($, api);
    return { app, requests };
}

test("comparison failure persists without an automatic retry; explicit retry recovers", async () => {
    const { app, requests } = comparisonHarness();
    const first = app.loadComparison();
    await app.loadComparison();
    assert.equal(requests.length, 1);
    requests[0].reject(Error("offline"));
    await first;
    assert.equal(app.comparison.failed, true);
    await app.loadComparison();
    assert.equal(requests.length, 1);
    const retry = app.retry();
    requests[1].resolve({ games: 2 });
    await retry;
    assert.equal(app.comparison.games, 2);
});

test("comparison ignores a late response from the previous game", async () => {
    const { app, requests } = comparisonHarness();
    const first = app.loadComparison();
    app.nextGame();
    const second = app.loadComparison();
    requests[1].resolve({ games: 3 });
    await second;
    requests[0].reject(Error("stale"));
    await first;
    assert.equal(app.comparison.games, 3);
    assert.equal(app.renders, 1);
});

class Element {
    constructor() {
        this.children = [];
        this.dataset = {};
        this.className = "";
        this.classList = { add: (...names) => this.className += " " + names.join(" ") };
    }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children.flatMap(c => c.fragment ? c.children : [c]); }
    get lastElementChild() { return this.children.at(-1); }
    get firstElementChild() { return this.children[0]; }
}

test("ranking keeps separate places before player identities and reuses unchanged rows", () => {
    const document = {
        createElement: () => new Element(),
        createDocumentFragment: () => Object.assign(new Element(), { fragment: true })
    };
    const rankList = new Function("$", "document", `
        let ownUsername = "p2", avatarVersion = 0;
        function profileLink(element, user) { element.user = user; }
        ${read("navigation.js")}
        return rankList;
    `)(() => ({}), document);
    const target = new Element();
    const players = Array.from({ length: 11 }, (_, i) => ({ user: "p" + (i + 1), score: 100 - i }));
    rankList(target, ["Бали"], players, p => [p.score], 0);
    const rows = target.children.slice(1);
    assert.equal(rows[0].children[0].children[0].textContent, 1);
    assert.equal(rows[0].children[1].user, "p1");
    assert.match(rows[0].children[0].children[0].className, /rank-place-1/);
    assert.match(rows[1].className, /own-rank/);
    assert.match(rows[9].children[0].children[0].className, /rank-place-top/);
    assert.doesNotMatch(rows[10].children[0].children[0].className, /rank-place-top/);
    assert.match(rows[0].children[2].className, /rank-metric/);
    rankList(target, ["Бали"], players, p => [p.score], 0);
    assert.equal(target.children[1], rows[0]);
});

test("floating search ignores stale results and does not alter friend sections", async () => {
    const elements = new Map(), requests = [];
    const $ = id => {
        if (!elements.has(id)) elements.set(id, Object.assign(new Element(), { value: "", addEventListener() {}, setAttribute() {} }));
        return elements.get(id);
    };
    const document = {
        createElement: () => new Element(),
        createTextNode: text => ({ textContent: text }),
        createDocumentFragment: () => Object.assign(new Element(), { fragment: true }),
        addEventListener() {}
    };
    const api = path => { const request = deferred(); requests.push({ path, ...request }); return request.promise; };
    const app = new Function("$", "document", "api", "clearTimeout", `
        let tab = "friends", epoch = 0, guestMode = false, verifiedAccount = true, ownUsername = "self";
        const playerAvatar = () => document.createElement("span");
        const message = () => {};
        ${read("friends.js")}
        return { searchPlayers, loadFriends, closePlayerSearch };
    `)($, document, api, () => {});
    const friends = app.loadFriends();
    requests[0].resolve([]);
    await friends;
    const sections = $("friend-list").children;
    $("player-search").value = "old";
    const old = app.searchPlayers();
    $("player-search").value = "new";
    const latest = app.searchPlayers();
    requests[2].resolve(["SELF", "new-player"]);
    await latest;
    requests[1].resolve(["old-player"]);
    await old;
    assert.equal($("player-results").children.length, 1);
    assert.equal($("player-results").children[0].children[1].textContent, "new-player");
    assert.equal($("friend-list").children, sections);
    assert.equal($("player-search-popup").hidden, false);
    const pending = app.searchPlayers();
    app.closePlayerSearch();
    requests[3].resolve(["late-player"]);
    await pending;
    assert.equal($("player-search-popup").hidden, true);
    assert.equal($("player-results").children.length, 0);
});

test("reload route preserves the exact page, challenge day and filters", () => {
    const normalize = new Function(read("route.js") + "\nreturn normalizeRoute;")();
    const play = normalize({ tab: "play", challengeDay: "2026-11-27" }, false);
    assert.equal(play.tab, "play");
    assert.equal(play.challengeDay, "2026-11-27");
    assert.equal(normalize(play, true).tab, "menu");
    const profile = normalize({ tab: "profile", profileUser: "other", profilePage: 3, profileLevel: "Hard" }, false);
    assert.equal(profile.profileUser, "other");
    assert.equal(profile.profilePage, 3);
    assert.equal(profile.profileLevel, "Hard");
    assert.equal(normalize({ tab: "ranking", rankMode: "all", rankMetric: "wins", rankLevel: "Expert" }, false).rankMetric, "wins");
    assert.equal(normalize({ tab: "challenge", calendarMonth: "2026-10" }, false).calendarMonth, "2026-10");
    assert.equal(normalize({ tab: "play", challengeDay: "2026-02-30" }, false).challengeDay, null);
    assert.equal(normalize({ tab: "invalid" }, false).tab, "menu");
    assert.equal(normalize({ tab: "friends" }, true).tab, "menu");
});
test("route storage is cleared on account reset and tolerates invalid JSON", () => {
    const storage = new Map(), elements = new Map();
    const sessionStorage = { getItem: k => storage.get(k) ?? null, setItem: (k,v) => storage.set(k,v), removeItem: k => storage.delete(k) };
    const $ = id => { if (!elements.has(id)) elements.set(id, { value: "" }); return elements.get(id); };
    const app = new Function("sessionStorage", "$", `
        let token = "session", guestMode = false, tab = "profile", challengeDay = null, calendarMonth = null, profileUser = "other", profilePage = 2;
        ${read("route.js")}
        return { saveRoute, readRoute, clearRoute, applyRoute };
    `)(sessionStorage, $);
    $("profile-level").value = "Medium";
    app.saveRoute();
    assert.equal(app.readRoute().profileUser, "other");
    assert.equal(app.readRoute().profileLevel, "Medium");
    app.clearRoute();
    assert.equal(app.readRoute().tab, "menu");
    sessionStorage.setItem("nesud.navigation.v1", "{");
    assert.equal(app.readRoute().tab, "menu");
});

test("select wheel changes unfocused options without wrapping or triggering duplicate changes", () => {
    const listeners = {}, dispatched = [];
    const root = { addEventListener: (name, fn) => { listeners[name] = fn; } };
    class MockEvent { constructor(type) { this.type = type; } }
    new Function("document", "Event", read("select-wheel.js"))(root, MockEvent);
    const select = {
        selectedIndex: 0, options: [{}, { disabled: true }, {}, {}],
        closest: () => select, matches: () => false, dispatchEvent: e => dispatched.push(e.type)
    };
    let time = 0;
    function wheel(deltaY, extra = {}) {
        let prevented = false;
        listeners.wheel({ target: select, deltaY, deltaX: 0, deltaMode: 0, timeStamp: time += 20,
            cancelable: true, preventDefault() { prevented = true; }, ...extra });
        return prevented;
    }
    assert.equal(wheel(100), true);
    assert.equal(select.selectedIndex, 2);
    assert.equal(dispatched.join(","), "input,change");
    wheel(100);
    wheel(100);
    assert.equal(select.selectedIndex, 3);
    assert.equal(dispatched.length, 4);
    wheel(-20);
    assert.equal(select.selectedIndex, 3);
    wheel(-20);
    assert.equal(select.selectedIndex, 2);
    assert.equal(wheel(-100, { ctrlKey: true }), false);
    assert.equal(select.selectedIndex, 2);
    select.matches = query => query === ":open";
    assert.equal(wheel(100), false);
    select.matches = query => query === ":disabled";
    assert.equal(wheel(100), false);
    assert.equal(dispatched.length, 6);
});
