import assert from "node:assert/strict";
import { HubConnectionBuilder, HttpTransportType } from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

const base = (process.env.NESUD_TEST_URL || "http://localhost:5080").replace(/\/$/, "");
async function post(path, body, token) {
    const response = await fetch(base + "/api/" + path, {
        method: "POST",
        headers: { "Content-Type": "application/json", ...(token ? { Authorization: "Bearer " + token } : {}) },
        body: JSON.stringify(body)
    });
    assert.equal(response.status, 200, path);
    return await response.json().catch(() => null);
}
const connections = [], tokens = [];
async function connect(token) {
    const hub = new HubConnectionBuilder()
        .withUrl(base + "/hubs/game", { transport: HttpTransportType.WebSockets, skipNegotiation: true })
        .withHubProtocol(new MessagePackHubProtocol()).build();
    connections.push(hub);
    await hub.start();
    if (token) assert.equal(await hub.invoke("Subscribe", token), true);
    return hub;
}
function changed(hub) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => { hub.off("GameChanged", handler); reject(Error("Missing push")); }, 5000);
        function handler(day) {
            clearTimeout(timer);
            hub.off("GameChanged", handler);
            resolve(day);
        }
        hub.on("GameChanged", handler);
    });
}
try {
    const a = (await post("guest", {})).token, b = (await post("guest", {})).token;
    tokens.push(a, b);
    const first = await connect(a), second = await connect(a), isolated = await connect(b), anonymous = await connect();
    assert.equal((await anonymous.invoke("Snapshot", null, false)).status, 401);
    assert.equal((await first.invoke("Snapshot", null, false)).data.empty, true);
    assert.equal((await first.invoke("Snapshot", "9999-12-31", false)).status, 400);
    let unrelated = 0;
    isolated.on("GameChanged", () => unrelated++);
    const newPush = changed(second);
    const result = await first.invoke("New", 0, null, 0, false, null);
    assert.equal(result.status, 200);
    const snapshot = result.data;
    assert.equal(await newPush, null);
    assert.equal(snapshot.values.length, 81);
    assert.equal(snapshot.solution, undefined);
    const cell = snapshot.givens.findIndex(value => value === 0);
    const movePush = changed(second);
    const move = await first.invoke("Move", snapshot.id, snapshot.revision, "enter", cell, 1, true, null, null);
    assert.equal(move.status, 200);
    assert.equal(move.data.delta, true);
    assert.equal(move.data.baseRevision, snapshot.revision);
    assert.ok(move.data.changes.notes);
    await movePush;
    const remote = (await second.invoke("Snapshot", null, false)).data;
    assert.equal(remote.revision, move.data.revision);
    assert.equal((await second.invoke("Move", snapshot.id, snapshot.revision, "enter", cell, 2, true, null, null)).status, 409);
    const restPush = changed(first);
    await post("web/action", { id: remote.id, revision: remote.revision, action: "pause" }, a);
    await restPush;
    assert.equal((await first.invoke("Snapshot", null, false)).data.paused, true);
    assert.equal(unrelated, 0);
    await post("logout", {}, a);
    assert.equal((await first.invoke("Snapshot", null, false)).status, 401);
    console.log("PASS binary SignalR, guest authorization, isolation, deltas, stale revision, REST push, logout");
} finally {
    await Promise.allSettled(connections.map(hub => hub.stop()));
    await Promise.allSettled(tokens.map(token => post("logout", {}, token)));
}
