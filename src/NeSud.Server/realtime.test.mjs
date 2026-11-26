import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";

const code = await readFile(new URL("./wwwroot/js/realtime.js", import.meta.url), "utf8");
const context = vm.createContext({});
vm.runInContext(code, context);
const merge = (current, data) => {
    context.current = current;
    context.data = data;
    return vm.runInContext("mergeGameDelta(current, data)", context);
};

test("snapshot and pulse pass through", () => {
    const snapshot = { id: "a", revision: 1, values: [0, 1] };
    assert.equal(merge(null, snapshot), snapshot);
    const pulse = { pulseOnly: true, revision: 1, seconds: 6 };
    assert.equal(merge(snapshot, pulse), pulse);
});
test("delta preserves unmodified fields", () => {
    const current = { id: "a", revision: 2, values: [0, 1], lives: 3, seconds: 1 };
    const result = merge(current, { delta: true, id: "a", baseRevision: 2, revision: 3,
        changes: { values: [2, 1], seconds: 5 } });
    assert.equal(result.revision, 3);
    assert.equal(result.lives, 3);
    assert.equal(result.seconds, 5);
    assert.deepEqual(result.values, [2, 1]);
});
test("wrong game or revision requires snapshot", () => {
    for (const current of [null, { id: "b", revision: 2 }, { id: "a", revision: 1 }])
        assert.throws(() => merge(current, { delta: true, id: "a", baseRevision: 2, revision: 3, changes: {} }),
            error => error.status === 409);
});
