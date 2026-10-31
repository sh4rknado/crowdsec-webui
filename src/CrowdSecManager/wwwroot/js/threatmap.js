/*
 * threatmap.js - live threat-map renderer for CrowdSec Manager
 *
 * - Renders a dark SOC-style world map on a <canvas>
 * - Uses an equirectangular projection (lat/lon -> x/y)
 * - Animates threat arcs from attacker source -> protected home
 * - Loads TopoJSON world-atlas from CDN with a dotted fallback if offline
 *
 * Public API (called via JS interop from Blazor):
 *   threatMap.init(canvasId, opts)
 *   threatMap.plotThreat(event)
 *   threatMap.updateStats(stats)
 *   threatMap.dispose()
 */
(function () {
    "use strict";
    const ns = {};
    window.threatMap = ns;

    let canvas, ctx, dpr;
    let width = 0, height = 0;
    let home = { lat: 50.85, lon: 4.35, label: "Home" };
    let geoAvailable = false;
    let worldFeatures = null;       // GeoJSON Feature[]
    let arcs = [];                  // active animated arcs
    let ripples = [];               // expanding circles on impact
    let sources = new Map();        // ipKey -> { x, y, count, lastSeen }
    let animId = null;
    let lastFrame = 0;
    const ARC_DURATION_MS = 3200;
    const MAX_ARCS = 60;
    const MAX_SOURCES = 300;
    const SOURCE_RETENTION_MS = 60_000;

    // ---- Equirectangular projection ----
    function project(lat, lon) {
        const x = (lon + 180) / 360 * width;
        const y = (90 - lat) / 180 * height;
        return { x, y };
    }

    // ---- World map loading ----
    async function loadWorldTopo() {
        const cdn = "https://cdn.jsdelivr.net/npm/world-atlas@2/countries-110m.json";
        try {
            const r = await fetch(cdn, { cache: "force-cache" });
            if (!r.ok) throw new Error("HTTP " + r.status);
            const topo = await r.json();
            // Minimal TopoJSON -> GeoJSON FeatureCollection
            const fc = topoToGeo(topo, "countries");
            return fc.features || [];
        } catch (e) {
            console.warn("threatmap: world-atlas CDN unreachable, using dot fallback", e);
            return null;
        }
    }

    // Tiny TopoJSON client - just enough for the countries-110m dataset.
    function topoToGeo(topology, objectName) {
        const obj = topology.objects[objectName];
        const arcs = topology.arcs;
        const transform = topology.transform;
        const coords = [];

        function decodeArc(i) {
            const a = arcs[i];
            const out = [];
            let x = 0, y = 0;
            for (let j = 0; j < a.length; j += 2) {
                x += a[j];
                y += a[j + 1];
                out.push([
                    x * transform.scale[0] + transform.translate[0],
                    y * transform.scale[1] + transform.translate[1]
                ]);
            }
            return out;
        }

        function lineArc(arcIndexes) {
            return arcIndexes.map(ai => {
                const i = ai < 0 ? ~ai : ai;
                const pts = decodeArc(i);
                if (ai < 0) pts.reverse();
                return pts;
            }).reduce((acc, cur) => acc.concat(cur), []);
        }

        function polygon(ring) { return [lineArc(ring)]; }
        function multiPolygon(rings) { return rings.map(polygon); }

        function geom(g) {
            if (g.type === "Polygon") return { type: "Polygon", coordinates: polygon(g.arcs) };
            if (g.type === "MultiPolygon") return { type: "MultiPolygon", coordinates: multiPolygon(g.arcs) };
            return null;
        }

        const features = obj.geometries.map(gt => ({
            type: "Feature",
            properties: gt.properties || {},
            geometry: geom(gt)
        })).filter(f => f.geometry);
        return { type: "FeatureCollection", features };
    }

    function drawWorldDots() {
        // Fallback: draw a dense dotted sphere using a pseudo-random pattern
        ctx.fillStyle = "rgba(120, 180, 220, 0.18)";
        for (let y = 0; y < height; y += 4) {
            for (let x = 0; x < width; x += 4) {
                const v = (Math.sin(x * 0.013) + Math.cos(y * 0.017)) * 0.5 + 0.5;
                if (v > 0.62) {
                    ctx.fillRect(x, y, 1, 1);
                }
            }
        }
    }

    function drawWorld() {
        ctx.fillStyle = "#0a0e1a";
        ctx.fillRect(0, 0, width, height);

        // Latitude grid
        ctx.strokeStyle = "rgba(120, 180, 220, 0.06)";
        ctx.lineWidth = 1;
        ctx.beginPath();
        for (let lat = -60; lat <= 60; lat += 30) {
            const y = (90 - lat) / 180 * height;
            ctx.moveTo(0, y); ctx.lineTo(width, y);
        }
        for (let lon = -180; lon <= 180; lon += 30) {
            const x = (lon + 180) / 360 * width;
            ctx.moveTo(x, 0); ctx.lineTo(x, height);
        }
        ctx.stroke();

        if (worldFeatures && worldFeatures.length) {
            ctx.fillStyle = "rgba(48, 80, 120, 0.55)";
            ctx.strokeStyle = "rgba(140, 200, 240, 0.35)";
            ctx.lineWidth = 1;
            for (const f of worldFeatures) {
                drawFeature(f.geometry);
            }
        } else {
            drawWorldDots();
        }
    }

    function drawFeature(geom) {
        if (!geom) return;
        if (geom.type === "Polygon") drawRings(geom.coordinates);
        else if (geom.type === "MultiPolygon") geom.coordinates.forEach(drawRings);
    }

    function drawRings(rings) {
        for (const ring of rings) {
            ctx.beginPath();
            ring.forEach((pt, i) => {
                const xy = projectLatLng(pt[1], pt[0]);
                if (i === 0) ctx.moveTo(xy.x, xy.y); else ctx.lineTo(xy.x, xy.y);
            });
            ctx.closePath();
            ctx.fill();
            ctx.stroke();
        }
    }

    function projectLatLng(lat, lon) {
        // lng/lat order in GeoJSON -> swap
        return project(lat, lon);
    }

    // ---- Home marker ----
    function drawHome() {
        const { x, y } = project(home.lat, home.lon);
        const t = Date.now() / 800;
        const r = 6 + Math.sin(t) * 2;
        // Pulsing ring
        const ringR = 8 + (Math.sin(t * 1.5) + 1) * 6;
        ctx.strokeStyle = "rgba(0, 220, 255, 0.5)";
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(x, y, ringR, 0, 2 * Math.PI);
        ctx.stroke();
        // Core dot
        ctx.fillStyle = "#00dcff";
        ctx.beginPath();
        ctx.arc(x, y, r, 0, 2 * Math.PI);
        ctx.fill();
        // Label
        ctx.font = "12px 'JetBrains Mono', monospace";
        ctx.fillStyle = "rgba(220,240,255,0.85)";
        ctx.fillText(home.label || "Home", x + 10, y - 8);
    }

    // ---- Arcs ----
    function severityColor(sev, alpha) {
        switch ((sev || "").toLowerCase()) {
            case "critical": return "rgba(255, 60, 60, " + alpha + ")";
            case "high": return "rgba(255, 120, 80, " + alpha + ")";
            case "medium": return "rgba(255, 200, 60, " + alpha + ")";
            case "low": return "rgba(120, 220, 180, " + alpha + ")";
            default: return "rgba(120, 180, 240, " + alpha + ")";
        }
    }

    ns.plotThreat = function (ev) {
        if (!ev || !ev.source) return;
        const src = project(ev.source.latitude, ev.source.longitude);
        const dst = project(home.lat, home.lon);

        const key = ev.sourceIp || (ev.source.latitude + "_" + ev.source.longitude);
        const cur = sources.get(key) || { x: src.x, y: src.y, count: 0, lastSeen: 0 };
        sources.delete(key); // Refresh insertion order so the oldest entry is least recently seen.
        if (sources.size >= MAX_SOURCES) {
            const oldestKey = sources.keys().next().value;
            if (oldestKey !== undefined) sources.delete(oldestKey);
        }
        cur.count++; cur.lastSeen = Date.now();
        cur.x = src.x; cur.y = src.y;
        sources.set(key, cur);

        arcs.push({
            sx: src.x, sy: src.y,
            dx: dst.x, dy: dst.y,
            start: performance.now(),
            rippled: false,
            sev: ev.severity || "Info",
            ip: ev.sourceIp,
            scenario: ev.scenario,
            country: ev.source.country || ev.source.countryIsoCode || "??"
        });
        if (arcs.length > MAX_ARCS) arcs.shift();
        document.getElementById("threatCount").textContent = String(arcs.length);
    };

    function drawArcs(t) {
        const now = t || performance.now();
        arcs = arcs.filter(a => (now - a.start) < ARC_DURATION_MS);
        for (const a of arcs) {
            const p = (now - a.start) / ARC_DURATION_MS;  // 0..1
            // Quadratic bezier via midpoint offset
            const mx = (a.sx + a.dx) / 2;
            const my = (a.sy + a.dy) / 2;
            const dx = a.dx - a.sx, dy = a.dy - a.sy;
            const dist = Math.hypot(dx, dy);
            const offset = Math.min(180, dist * 0.35);
            const nx = -dy / (dist || 1), ny = dx / (dist || 1);
            const cpx = mx + nx * offset;
            const cpy = my + ny * offset;

            // Trail (fading head)
            ctx.lineWidth = 2;
            ctx.strokeStyle = severityColor(a.sev, 0.6);
            ctx.shadowColor = severityColor(a.sev, 0.9);
            ctx.shadowBlur = 8;
            ctx.beginPath();
            ctx.moveTo(a.sx, a.sy);

            const steps = 24;
            const head = Math.min(1, p * 1.1);
            let lastX = a.sx, lastY = a.sy;
            for (let i = 1; i <= steps; i++) {
                const u = (i / steps) * head;
                const px = (1 - u) * (1 - u) * a.sx + 2 * (1 - u) * u * cpx + u * u * a.dx;
                const py = (1 - u) * (1 - u) * a.sy + 2 * (1 - u) * u * cpy + u * u * a.dy;
                ctx.lineTo(px, py);
                lastX = px; lastY = py;
            }
            ctx.stroke();
            ctx.shadowBlur = 0;

            // Source pulsing dot
            if (p < 0.6) {
                ctx.fillStyle = severityColor(a.sev, 1);
                ctx.beginPath();
                ctx.arc(a.sx, a.sy, 3 + Math.sin(now / 100) * 1.5, 0, 2 * Math.PI);
                ctx.fill();
            }

            // Impact ripple near destination
            if (p > 0.7 && p < 1 && !a.rippled) {
                ripples.push({ x: a.dx, y: a.dy, start: now, color: severityColor(a.sev, 0.8) });
                a.rippled = true;
            }
        }

        // Draw ripples
        ripples = ripples.filter(r => (now - r.start) < 800);
        for (const r of ripples) {
            const age = (now - r.start) / 800;
            const rad = 4 + age * 30;
            ctx.strokeStyle = r.color.replace(/[\d.]+\)$/, (1 - age) + ")");
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(r.x, r.y, rad, 0, 2 * Math.PI);
            ctx.stroke();
        }
    }

    function drawSources() {
        for (const [key, s] of sources) {
            const ageMs = Date.now() - s.lastSeen;
            if (ageMs > SOURCE_RETENTION_MS) {
                sources.delete(key);
                continue;
            }
            const age = ageMs / 1000;
            const alpha = Math.max(0, 1 - age / 60);
            ctx.fillStyle = "rgba(255, 100, 100, " + (alpha * 0.6) + ")";
            ctx.beginPath();
            ctx.arc(s.x, s.y, 2 + Math.min(4, s.count * 0.4), 0, 2 * Math.PI);
            ctx.fill();
        }
    }

    function frame(t) {
        if (!ctx) return;
        // Light trail effect: redraw world each frame (cheap at 60fps)
        drawWorld();
        drawSources();
        drawArcs(t);
        const counter = document.getElementById("threatCount");
        if (counter) counter.textContent = String(arcs.length);
        drawHome();
        animId = requestAnimationFrame(frame);
    }

    function resize() {
        if (!canvas) return;
        dpr = window.devicePixelRatio || 1;
        const rect = canvas.getBoundingClientRect();
        width = Math.max(800, rect.width || canvas.width);
        height = Math.max(400, rect.height || canvas.height);
        canvas.width = width * dpr;
        canvas.height = height * dpr;
        canvas.style.width = width + "px";
        canvas.style.height = height + "px";
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    ns.init = async function (canvasId, opts) {
        canvas = document.getElementById(canvasId);
        if (!canvas) { console.warn("threatmap: canvas not found:", canvasId); return; }
        ctx = canvas.getContext("2d");
        if (opts && opts.home) home = opts.home;
        if (opts && typeof opts.geoAvailable !== "undefined") geoAvailable = opts.geoAvailable;

        resize();
        window.addEventListener("resize", resize);

        worldFeatures = await loadWorldTopo();
        if (animId) cancelAnimationFrame(animId);
        animId = requestAnimationFrame(frame);
    };

    ns.updateStats = function (stats) { /* Blazor renders stats panel directly */ };

    ns.dispose = function () {
        if (animId) cancelAnimationFrame(animId);
        animId = null;
        window.removeEventListener("resize", resize);
        arcs = []; ripples = []; sources = new Map();
    };
})();
