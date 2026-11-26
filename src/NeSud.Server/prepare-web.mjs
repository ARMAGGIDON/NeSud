import { mkdir, copyFile } from "node:fs/promises";

const output = new URL("./wwwroot/vendor/", import.meta.url);
await mkdir(output, { recursive: true });
for (const [pkg, file] of [
    ["signalr", "signalr.js"],
    ["signalr-protocol-msgpack", "signalr-protocol-msgpack.js"]
]) {
    await copyFile(new URL(`./node_modules/@microsoft/${pkg}/dist/browser/${file}`, import.meta.url),
        new URL(file, output));
}
