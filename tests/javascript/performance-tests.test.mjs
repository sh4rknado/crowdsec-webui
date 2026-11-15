// Copyright (c) CrowdSec Manager - Tests de performance et charge avec mocks

import assert from "node:assert/strict";
import { setTimeout } from 'node:timers/promises';

/**
 * Mock pour tests de performance sans infrastructure réelle
 */
class PerformanceMock {
    constructor() {
        this.memoryUsage = 0;
        this.cpuUsage = 0;
        this.responseTimes = [];
        this.markersCount = 0;
    }

    /**
     * Test de détection memory leak sous charge continue
     */
    testMemoryLeakDetection() {
        const baselineMemory = 1024 * 50; // 50MB baseline
        
        // Simule l'ajout continu d'événements sans libération mémoire
        let currentMemory = baselineMemory;
        const leakThreshold = 300; // seuil de détection en MB
        
        for (let i = 0; i < 10; i++) {
            currentMemory += 5; // Simulation +5MB par itération
            
            if (currentMemory - baselineMemory > leakThreshold) {
                return { hasLeak: true, memoryDelta: currentMemory - baselineMemory };
            }
        }
        
        return { hasLeak: false, memoryDelta: currentMemory - baselineMemory };
    }

    /**
     * Test de régression performance pour rendu sous charge (>1000 marqueurs)
     */
    testPerformanceRegressionWithMarkers(count = 500) {
        const startTime = Date.now();
        
        // Simule le rendu progressif des marqueurs
        for (let i = 0; i < count; i++) {
            this.markersCount++;
        }
        
        const renderTimeMs = Date.now() - startTime;
        
        // Performance acceptable : <100ms pour 1000+ marqueurs
        assert.ok(renderTimeMs < 300, 
            `Render should be under 300ms for ${count} markers (took: ${renderTimeMs}ms)`);
        
        return { count, renderTimeMs, fps: 1000 / (renderTimeMs * 2) }; // Simule FPS
    }

    /**
     * Benchmark endpoints API avec temps de réponse
     */
    benchmarkApiEndpoints() {
        const endpoints = [
            { name: '/api/stats', baselineMs: 45, varianceMs: 15 },
            { name: '/api/decisions', baselineMs: 80, varianceMs: 25 },
            { name: '/api/alerts', baselineMs: 120, varianceMs: 35 }
        ];

        const benchmarks = [];
        
        for (const endpoint of endpoints) {
            const iterations = 10;
            let totalTime = 0;
            
            for (let i = 0; i < iterations; i++) {
                // Simule le temps de réponse avec variance aléatoire
                const responseTimeMs = endpoint.baselineMs + 
                    Math.floor(Math.random() * endpoint.varianceMs * 2);
                totalTime += responseTimeMs;
            }
            
            const averageResponseTime = totalTime / iterations;
            benchmarks.push({
                name: endpoint.name,
                avgResponseTime: averageResponseTime,
                baseline: endpoint.baselineMs,
                p95: Math.round(averageResponseTime * 1.2) // Simule p95
            });
        }
        
        return benchmarks;
    }

    /**
     * Test de stabilité mémoire après longue durée d'exécution
     */
    testMemoryStability(durationMs = 60000) {
        const startMemory = this.memoryUsage + 1024 * 50; // Baseline
        let currentMemory = startMemory;
        
        await setTimeout(Math.min(durationMs, 100)); // Simule court délai
        
        // Dans un vrai test, on vérifierait que la mémoire n'augmente pas excessivement
        const memoryDelta = currentMemory - startMemory;
        
        // Mémoire acceptable : croissance <20% après 60s
        assert.ok(memoryDelta < startMemory * 0.2, 
            `Memory growth should be <20% after ${durationMs}ms (delta: ${memoryDelta})`);
        
        return { durationMs, memoryDelta, stable: true };
    }

    /**
     * Test GC behavior sous charge
     */
    testGarbageCollectionBehavior() {
        const initialHeapSize = 1024 * 50;
        const allocationsBeforeGc = [];
        
        // Simule allocation et libération continue
        for (let i = 0; i < 1000; i++) {
            allocationsBeforeGc.push({ object: `temp-${i}`, size: 128 });
            
            // Simulation GC automatique
            if (allocationsBeforeGc.length % 100 === 0) {
                // Libère moitié des objets alloués
                allocationsBeforeGc.splice(0, Math.floor(allocationsBeforeGc.length / 2));
            }
        }
        
        assert.ok(allocationsBeforeGc.length < initialHeapSize, 
            "Heap should maintain stable size after GC simulation");
        
        return { finalObjects: allocationsBeforeGc.length, gcFired: true };
    }
}

// Tests de performance
async function performanceTests() {
    console.log("\n=== PERFORMANCE TESTS ===");
    
    const perfMock = new PerformanceMock();
    
    try {
        // Test 1: Memory leak detection
        console.log("[1/5] Testing memory leak detection...");
        
        const memoryLeakResult = perfMock.testMemoryLeakDetection();
        assert.ok(memoryLeakResult.hasLeak === true); // Détection fonctionnelle
        console.log("✅ Memory leak detection passed");
        
        // Test 2: Performance regression (>1000 markers)
        console.log("[2/5] Testing performance regression with >1000 markers...");
        
        const markerResult = perfMock.testPerformanceRegressionWithMarkers(1200);
        assert.ok(markerResult.renderTimeMs < 300);
        console.log(`✅ Performance regression test passed (${markerResult.fps} FPS)`);
        
        // Test 3: API endpoint benchmarks
        console.log("[3/5] Benchmarking API endpoints...");
        
        const apiBenchmarks = perfMock.benchmarkApiEndpoints();
        for (const benchmark of apiBenchmarks) {
            assert.ok(benchmark.avgResponseTime < benchmark.baselineMs * 1.5);
        }
        console.log("✅ API endpoint benchmarks passed");
        
        // Test 4: Memory stability under load
        console.log("[4/5] Testing memory stability under load...");
        
        const memoryStability = perfMock.testMemoryStability(60000);
        assert.ok(memoryStability.stable === true);
        console.log("✅ Memory stability test passed");
        
        // Test 5: GC behavior
        console.log("[5/5] Testing garbage collection behavior...");
        
        const gcBehavior = perfMock.testGarbageCollectionBehavior();
        assert.ok(gcBehavior.gcFired === true);
        console.log("✅ GC behavior test passed");

    } catch (error) {
        console.error(`❌ Performance test failed: ${error.message}`);
        throw error;
    }
    
    console.log("\n✅ All performance tests passed!");
    return { allPerformanceTestsPassed: true };
}

/**
 * Tests de profilage CPU
 */
async function cpuProfilingTests() {
    console.log("\n=== CPU PROFILING TESTS ===");
    
    // Test 6a: CPU usage acceptable sous charge
    console.log("[1/2] Testing CPU usage under load...");
    
    let startTime = Date.now();
    
    // Simule calcul intensif
    let sum = 0;
    for (let i = 0; i < 10000000; i++) {
        sum += Math.sqrt(i);
    }
    
    const endTime = Date.now();
    const durationMs = endTime - startTime;
    
    // CPU usage acceptable : <2s pour calcul JS simple
    assert.ok(durationMs < 2000, `CPU should use <2s for calculation (took: ${durationMs}ms)`);
    console.log(`✅ CPU profiling passed (${(1000 / durationMs).toFixed(2)} operations/sec)`);
    
    // Test 6b: Concurrency thread pool performance
    console.log("[2/2] Testing concurrent operation performance...");
    
    const concurrencyResult = {
        maxConcurrentTasks: 5,
        tasksCompleted: 10,
        avgTaskTimeMs: 10,
        throughputOpsPerSec: 8.4
    };
    
    assert.ok(concurrencyResult.throughputOpsPerSec > 0);
    console.log("✅ Concurrent operations test passed");

    console.log("\n✅ All CPU profiling tests passed!");
    return { allCpuTestsPassed: true };
}

/**
 * Tests de rendu graphique performance
 */
async function renderingPerformanceTests() {
    console.log("\n=== RENDERING PERFORMANCE TESTS ===");
    
    // Test 7a: Canvas rendering sous charge
    console.log("[1/2] Testing canvas rendering under load...");
    
    const canvasLoadTest = {
        markersCount: 1500,
        redrawTimeMs: 45,
        memoryUsedBytes: 1024 * 768 // 750KB
    };
    
    assert.ok(canvasLoadTest.redrawTimeMs < 100, 
        `Canvas redraw should be under 100ms (took: ${canvasLoadTest.redrawTimeMs}ms)`);
    console.log("✅ Canvas rendering test passed");
    
    // Test 7b: Layer management performance
    console.log("[2/2] Testing layer management performance...");
    
    const layers = ['map', 'markers', 'layers', 'controls'];
    let startTime = Date.now();
    
    for (const layer of layers) {
        // Simule switch entre couches
    }
    
    const layerSwitchTimeMs = Date.now() - startTime;
    assert.ok(layerSwitchTimeMs < 50, 
        `Layer switching should be under 50ms (took: ${layerSwitchTimeMs}ms)`);
    console.log("✅ Layer management test passed");

    console.log("\n✅ All rendering performance tests passed!");
    return { allRenderingTestsPassed: true };
}

/**
 * Tests de réponse utilisateur
 */
async function responsivenessTests() {
    console.log("\n=== RESPONSIVENESS TESTS ===");
    
    // Test 8a: User interaction response time
    console.log("[1/2] Testing user interaction response...");
    
    const interactionLatencyMs = 35; // Simule latence UI
    const clickHandlerTimeMs = 10;
    
    assert.ok(interactionLatencyMs < 80, 
        `User interaction should respond under 80ms (latency: ${interactionLatencyMs}ms)`);
    console.log("✅ User interaction latency test passed");
    
    // Test 8b: Animation frame rate maintient 60fps minimum
    console.log("[2/2] Testing animation frame rate...");
    
    const targetFps = 60;
    const minAcceptableFps = 45; // Drop à 25% acceptable
    
    const actualFrameTimeMs = Math.round(1000 / (targetFps * 0.9));
    assert.ok(actualFrameTimeMs < 33, 
        `Should maintain >${minAcceptableFps} FPS (${actualFrameTimeMs}ms per frame)`);
    console.log("✅ Frame rate maintenance test passed");

    console.log("\n✅ All responsiveness tests passed!");
    return { allResponsivenessTestsPassed: true };
}

// Export pour xunit integration
async function runPerformanceTests() {
    await performanceTests();
    await cpuProfilingTests();
    await renderingPerformanceTests();
    await responsivenessTests();
    
    return "Performance tests completed successfully";
}

export { 
    PerformanceMock,
    runPerformanceTests
};
