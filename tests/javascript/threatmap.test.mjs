import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import vm from "node:vm";
import test from "node:test";
import { fileURLToPath } from "node:url";

const scriptPath = fileURLToPath(new URL("../../src/CrowdSecManager/wwwroot/js/threatmap.js", import.meta.url));
const threatMapSource = readFileSync(scriptPath, "utf8");
const require = createRequire(import.meta.url);
const topojson = require("../../src/CrowdSecManager/wwwroot/lib/topojson-client.min.js");
const worldTopology = JSON.parse(readFileSync(new URL("../../src/CrowdSecManager/wwwroot/lib/countries-110m.json", import.meta.url), "utf8"));

function createHarness({ fetchImpl, fireTimeoutImmediately = false } = {}) {
    const clock = { now: 1000, performance: 0 };
    const maps = [];
    const frames = [];
    const cancelledFrames = [];
    const counter = { textContent: "0" };
    const ctx = new Proxy({
        setTransform() {}, fillRect() {}, beginPath() {}, moveTo() {}, lineTo() {},
        stroke() {}, fill() { this.fills++; }, closePath() {}, fillText() {}, impactRings: 0, fills: 0,
        arc() {
            if (this.strokeStyle?.startsWith("rgba(255, 120, 80")) this.impactRings++;
        }
    }, { set(target, key, value) { target[key] = value; return true; } });
    const canvas = {
        width: 1200,
        height: 600,
        style: {},
        getContext: () => ctx,
        getBoundingClientRect: () => ({ width: 1200, height: 600 })
    };
    class ObservedMap extends Map {
        constructor(...args) { super(...args); maps.push(this); }
    }
    class FakeDate extends Date {
        static now() { return clock.now; }
    }
    const document = {
        getElementById(id) {
            if (id === "threatCanvas") return canvas;
            if (id === "threatCount") return counter;
            return null;
        }
    };
    const window = {
        devicePixelRatio: 1,
        topojson,
        addEventListener() {},
        removeEventListener() {}
    };
    const sandbox = {
        window,
        document,
        Map: ObservedMap,
        Date: FakeDate,
        AbortController,
        performance: { now: () => clock.performance },
        requestAnimationFrame: callback => { frames.push(callback); return frames.length; },
        cancelAnimationFrame: id => { cancelledFrames.push(id); },
        setTimeout: fireTimeoutImmediately
            ? callback => { callback(); return 0; }
            : (callback, delay) => setTimeout(callback, delay),
        clearTimeout: timeoutId => clearTimeout(timeoutId),
        fetch: fetchImpl || (async () => { throw new Error("offline test fixture"); }),
        console: { warn() {}, log() {}, error() {} }
    };
    vm.runInNewContext(threatMapSource, sandbox, { filename: "threatmap.js" });
    return { api: window.threatMap, clock, maps, frames, cancelledFrames, counter, ctx };
}

async function sourceMarkersStayBounded() {
    const { api, maps } = createHarness();

    for (let i = 0; i < 1000; i++) {
        api.plotThreat({ sourceIp: `203.0.113.${i}`, source: { latitude: 10, longitude: 20 } });
    }

    assert.equal(maps[0].size, 300);
    assert.equal(maps[0].has("203.0.113.0"), false);
    assert.equal(maps[0].has("203.0.113.999"), true);
}
test("source markers stay bounded and evict the least recently seen IP", sourceMarkersStayBounded);

async function expiredStateIsRemoved() {
    const { api, clock, frames, maps, counter } = createHarness();
    await api.init("threatCanvas", {});
    api.plotThreat({
        sourceIp: "203.0.113.8",
        source: { latitude: 10, longitude: 20 },
        severity: "High"
    });

    assert.equal(counter.textContent, "1");
    clock.performance = 4000;
    clock.now = 62_000;
    frames.at(-1)(clock.performance);

    assert.equal(counter.textContent, "0");
    assert.equal(maps[0].size, 0);
}
test("expired source markers are removed and expired arcs update the active counter", expiredStateIsRemoved);

async function eachArcCreatesOneImpactRipple() {
    const { api, clock, frames, ctx } = createHarness();
    await api.init("threatCanvas", {});
    api.plotThreat({ sourceIp: "203.0.113.9", source: { latitude: 0, longitude: 20 }, severity: "High" });
    for (const time of [2300, 2400, 2500, 2600, 2700]) {
        clock.performance = time;
        frames.at(-1)(time);
    }
    assert.equal(ctx.impactRings, 5);
}
test("each animated arc creates at most one impact ripple", eachArcCreatesOneImpactRipple);

async function mapBackgroundFallsBackWhenFetchTimesOut() {
    let requestSignal;
    const { api, frames } = createHarness({
        fireTimeoutImmediately: true,
        fetchImpl: async (_url, options) => {
            requestSignal = options.signal;
            if (requestSignal.aborted) throw new Error("request aborted by timeout");
            throw new Error("expected timeout to abort request");
        }
    });

    await api.init("threatCanvas", {});

    assert.equal(requestSignal.aborted, true);
    assert.equal(frames.length, 1);
}
test("world map fetch timeout uses the dot fallback", mapBackgroundFallsBackWhenFetchTimesOut);

async function worldMapLoadsFromBundledData() {
    let requestedUrl;
    const { api, frames, ctx } = createHarness({
        fetchImpl: async url => {
            requestedUrl = url;
            return { ok: true, json: async () => worldTopology };
        }
    });

    await api.init("threatCanvas", {});
    frames.at(-1)(0);

    assert.equal(requestedUrl, "lib/countries-110m.json");
    assert.equal(ctx.fills, 287);
    assert.doesNotMatch(threatMapSource, /cdn\.jsdelivr\.net|world-atlas@/);
}
test("world map uses bundled libraries and local country data without a CDN", worldMapLoadsFromBundledData);

async function invalidThreatEventsAreIgnored() {
    const { api, counter } = createHarness();
    api.plotThreat(null);
    api.plotThreat({ sourceIp: "203.0.113.1" });
    assert.equal(counter.textContent, "0");
}
test("invalid threat events do not create arcs", invalidThreatEventsAreIgnored);

async function disposeStopsAnimationAndClearsState() {
    const { api, cancelledFrames, maps } = createHarness();
    await api.init("threatCanvas", {});
    api.plotThreat({ sourceIp: "203.0.113.2", source: { latitude: 1, longitude: 2 } });
    api.dispose();
    assert.deepEqual(cancelledFrames, [1]);
    api.plotThreat({ sourceIp: "203.0.113.3", source: { latitude: 2, longitude: 3 } });
    assert.equal(maps[1].size, 1);
}
test("dispose clears threat marker state and stops the map animation", disposeStopsAnimationAndClearsState);

export async function runThreatMapAssertions() {
    await sourceMarkersStayBounded();
    await expiredStateIsRemoved();
    await eachArcCreatesOneImpactRipple();
    await mapBackgroundFallsBackWhenFetchTimesOut();
    await worldMapLoadsFromBundledData();
    await invalidThreatEventsAreIgnored();
    await disposeStopsAnimationAndClearsState();
    return "7 threat map JavaScript assertions passed";
}
