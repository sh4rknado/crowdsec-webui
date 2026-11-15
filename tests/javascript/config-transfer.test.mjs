import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const scriptPath = fileURLToPath(new URL("../../src/CrowdSecManager/wwwroot/js/config-transfer.js", import.meta.url));
const source = readFileSync(scriptPath, "utf8");

function createHarness() {
    const state = { appended: [], clicked: 0, removed: 0, revoked: [], blobs: [] };
    const anchor = {
        click() { state.clicked++; },
        remove() { state.removed++; }
    };
    const context = {
        window: {},
        Blob,
        URL: {
            createObjectURL(blob) {
                state.blobs.push(blob);
                return `blob:test-${state.blobs.length}`;
            },
            revokeObjectURL(url) { state.revoked.push(url); }
        },
        document: {
            createElement(tag) {
                assert.equal(tag, "a");
                return anchor;
            },
            body: { appendChild(element) { state.appended.push(element); } }
        }
    };
    vm.runInNewContext(source, context);
    return { api: context.window.configTransfer, state, anchor };
}

test("download creates a ZIP blob and clicks a temporary download link", () => {
    const { api, state, anchor } = createHarness();

    api.download("crowdsec-config.zip", new Uint8Array([1, 2, 3]));

    assert.equal(state.blobs[0].type, "application/zip");
    assert.equal(anchor.download, "crowdsec-config.zip");
    assert.equal(anchor.href, "blob:test-1");
    assert.deepEqual(state.appended, [anchor]);
    assert.equal(state.clicked, 1);
    assert.equal(state.removed, 1);
    assert.deepEqual(state.revoked, ["blob:test-1"]);
});

test("downloadText creates a UTF-8 text blob and revokes the temporary URL", () => {
    const { api, state, anchor } = createHarness();

    api.downloadText("config.yaml", new TextEncoder().encode("key: value\n"));

    assert.equal(state.blobs[0].type, "text/plain;charset=utf-8");
    assert.equal(anchor.download, "config.yaml");
    assert.equal(anchor.href, "blob:test-1");
    assert.equal(state.clicked, 1);
    assert.equal(state.removed, 1);
    assert.deepEqual(state.revoked, ["blob:test-1"]);
});
