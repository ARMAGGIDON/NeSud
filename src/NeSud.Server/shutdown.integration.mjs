import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createServer } from "node:net";
import { fileURLToPath } from "node:url";
import { HubConnectionBuilder, HttpTransportType, LogLevel } from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

const manual = process.argv.includes("--manual");
if (process.platform === "win32" && !manual) {
    console.error("Windows: run the server in another console, then npm run test:shutdown -- --manual");
    process.exit(2);
}
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(condition, timeout, message) {
    const until = Date.now() + timeout;
    while (Date.now() < until) {
        if (await condition()) return;
        await sleep(100);
    }
    throw Error(message);
}
async function availablePort() {
    const server = createServer();
    await new Promise((resolve, reject) => {
        server.once("error", reject);
        server.listen(0, "127.0.0.1", resolve);
    });
    const port = server.address().port;
    await new Promise(resolve => server.close(resolve));
    return port;
}
const directory = manual ? null : await mkdtemp(join(tmpdir(), "nesud-shutdown-"));
const base = manual ? (process.env.NESUD_TEST_URL || "http://localhost:5080").replace(/\/$/, "")
    : "http://127.0.0.1:" + await availablePort();
let child, exited = false, exitCode, exitSignal, log = "";
const hubs = [], closed = new Set();
async function healthy() {
    try { return (await fetch(base + "/api/health", { signal: AbortSignal.timeout(500) })).ok; }
    catch { return false; }
}
try {
    if (!manual) {
        const root = fileURLToPath(new URL("./", import.meta.url));
        child = spawn("dotnet", [join(root, "bin", "Debug", "net10.0", "NeSud.Server.dll"),
            "--urls", base, "--Database", join(directory, "server.db")], { cwd: root, stdio: ["ignore", "pipe", "pipe"] });
        child.on("error", error => { log += error.message; exited = true; });
        child.on("exit", (code, signal) => { exited = true; exitCode = code; exitSignal = signal; });
        for (const stream of [child.stdout, child.stderr]) stream.on("data", chunk => { log = (log + chunk).slice(-12000); });
    }
    await waitFor(healthy, 20000, "Server did not become healthy. Build the server first.");
    for (let i = 0; i < 2; i++) {
        const hub = new HubConnectionBuilder()
            .withUrl(base + "/hubs/game", { transport: HttpTransportType.WebSockets, skipNegotiation: true })
            .withHubProtocol(new MessagePackHubProtocol()).configureLogging(LogLevel.None).build();
        hubs.push(hub);
        hub.onclose(() => closed.add(hub));
        await hub.start();
        assert.equal((await hub.invoke("Snapshot", null, false)).status, 401);
    }
    console.log("READY: 2 live MessagePack WebSockets (no account or game writes).");
    if (manual) console.log("Press Ctrl+C once in the SERVER console now. Waiting for disconnection.");
    else {
        // SIGINT targets the actual server process, not a dotnet run wrapper.
        assert.ok(child.kill("SIGINT"));
    }
    const started = Date.now();
    await waitFor(() => closed.size === hubs.length, manual ? 60000 : 8000, "WebSocket shutdown timed out.");
    const disconnected = Date.now();
    await waitFor(async () => !(await healthy()), 5000, "HTTP listener still active.");
    if (!manual) {
        await waitFor(() => exited, Math.max(100, 10000 - (Date.now() - started)), "Server process did not exit.");
        assert.equal(exitSignal, null, "Server was terminated by a signal instead of shutting down normally");
        assert.equal(exitCode, 0, log);
    }
    console.log("PASS: WebSockets closed, HTTP listener stopped" +
        (manual ? "; verify that the server console returned to its prompt." : "; process exited normally.") +
        " Disconnect-to-listener check: " + (Date.now() - disconnected) + " ms.");
} catch (error) {
    if (log) console.error(log);
    throw error;
} finally {
    await Promise.allSettled(hubs.map(hub => hub.stop()));
    if (child && !exited) {
        child.kill("SIGKILL");
        await waitFor(() => exited, 5000, "Test process cleanup timed out.");
    }
    if (directory) await rm(directory, { recursive: true, force: true });
}
