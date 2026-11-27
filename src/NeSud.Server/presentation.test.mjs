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

test("friend search ignores a stale response and excludes the current user", async () => {
    const elements = new Map(), requests = [];
    const $ = id => {
        if (!elements.has(id)) elements.set(id, Object.assign(new Element(), { value: "", addEventListener() {} }));
        return elements.get(id);
    };
    const document = {
        createElement: () => new Element(),
        createTextNode: text => ({ textContent: text }),
        createDocumentFragment: () => Object.assign(new Element(), { fragment: true })
    };
    const api = path => {
        const request = deferred();
        requests.push({ path, ...request });
        return request.promise;
    };
    const load = new Function("$", "document", "api", `
        let tab = "friends", epoch = 0, guestMode = false, verifiedAccount = true, ownUsername = "self";
        const playerAvatar = () => document.createElement("span");
        const message = () => {};
        ${read("friends.js")}
        return loadFriends;
    `)($, document, api);
    $("player-search").value = "old";
    const old = load();
    $("player-search").value = "new";
    const latest = load();
    requests[2].resolve([]);
    requests[3].resolve(["SELF", "new-player"]);
    await latest;
    requests[0].resolve([]);
    requests[1].resolve(["old-player"]);
    await old;
    const rows = $("player-results").children;
    assert.equal(rows.length, 1);
    assert.equal(rows[0].children[1].textContent, "new-player");
    assert.equal($("player-search-status").textContent, "Знайдено: 1");
    assert.equal($("friend-list").children.length, 3);
});
