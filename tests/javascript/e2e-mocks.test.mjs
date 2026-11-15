// Copyright (c) CrowdSec Manager - Tests E2E avec mocks intelligents
// Ces tests simulent le comportement complet de l'application CrowdSec

import assert from "node:assert/strict";
import { readFileSync, writeFileSync, existsSync, mkdirSync } from "node:fs";

class MockCrowdSecApiService {
    constructor() {
        this.decisions = [
            { id: 1, target: "malicious-scenario", until: new Date(Date.now() + 4 * 3600000).toISOString(), reason: "malicious" },
            { id: 2, target: "policy-violation", until: new Date(Date.now() + 8 * 3600000).toISOString(), reason: "policy" }
        ];
        this.alerts = [
            { id: 1, scenario: "malicious-scenario", source: { value: "192.168.1.100", scope: "Ip", country: "FR" }, timestamp: new Date() }
        ];
    }

    async getSnapshotStatsAsync() {
        return { totalAlerts: 500, decidedRate: 98.5, rateLimited: 120, uniqueSources: 342 };
    }

    async getDecisionsAsync() {
        return this.decisions;
    }

    async listAlertsAsync(limit, ipFilter, scenarioFilter, since, until, type) {
        return this.alerts.filter(alert => !ipFilter || alert.source.value === ipFilter);
    }

    async addDecisionAsync(target, duration, reason, type) {
        const newId = Math.max(...this.decisions.map(d => d.id)) + 1;
        this.decisions.push({
            id: newId,
            target,
            until: new Date(Date.now() + parseInt(duration.replace('h', '') * 3600000)).toISOString(),
            reason
        });
        return { success: true, duration: parseFloat(duration) / 3600 };
    }

    async deleteDecisionAsync(id) {
        this.decisions = this.decisions.filter(d => d.id !== id);
        return { success: true };
    }
}

class MockCrowdSecCliService {
    constructor(apiService) {
        this.api = apiService;
        this.history = [];
    }

    async hubUpdateAsync() {
        console.log("Mock: Hub updated successfully");
        return { success: true, duration: 0.5 };
    }

    async listAlertsAsync(limit, ipFilter, scenarioFilter, since, until, type) {
        console.log(`Mock: Listing alerts with filters: ${ipFilter || 'none'}, ${scenarioFilter || 'none'}`);
        return await this.api.listAlertsAsync(limit, ipFilter, scenarioFilter, since, until, type);
    }

    async runAsync(command, ...args) {
        console.log(`Mock: Running command: ${command} ${args.join(' ')}`);
        const result = { success: true, duration: 0.1, output: "Command executed" };
        this.history.push({ command, args, timestamp: Date.now() });
        return result;
    }

    async addDecisionAsync(target, duration, reason, type) {
        console.log(`Mock: Adding decision for ${target}`);
        return await this.api.addDecisionAsync(target, duration, reason, type);
    }

    async deleteDecisionAsync(id) {
        console.log(`Mock: Deleting decision ${id}`);
        return await this.api.deleteDecisionAsync(id);
    }

    async pruneBouncersAsync(threshold = "1h", force = false) {
        console.log(`Mock: Pruning bouncers with threshold ${threshold}, force=${force}`);
        return { success: true, pruned: 3 };
    }

    async pruneMachinesAsync(threshold = "10m", force = false) {
        console.log(`Mock: Pruning machines with threshold ${threshold}, force=${force}`);
        return { success: true, pruned: 2 };
    }
}

class MockGeoIpService {
    constructor() {
        this.databaseAvailable = true;
        this.home = { latitude: 48.8566, longitude: 2.3522, city: "Paris" };
    }

    get isDatabaseAvailable() {
        return this.databaseAvailable;
    }

    async refreshDatabaseAsync() {
        console.log("Mock: GeoIP database refreshed");
        return { success: true, recordsLoaded: 71000000 };
    }

    normalizeAddress(ip) {
        // Simpler normalization for testing
        return ip.replace(/^::ffff:/, "");
    }
}

class MockEventBus {
    constructor() {
        this.recentEvents = [];
        this.stats = new Map();
    }

    async addEvent(event) {
        const eventId = Math.random().toString(36).substr(2, 9);
        event.id = eventId;
        event.timestamp = Date.now();
        this.recentEvents.push({ ...event });
        return eventId;
    }

    getSnapshot() {
        return {
            stats: { totalAlerts: this.recentEvents.length, uniqueSources: new Set(this.recentEvents.map(e => e.sourceIp)).size },
            recentEvents: this.recentEvents.slice(-50)
        };
    }
}

// Test E2E avec mocks
async function e2eDashboardFlow() {
    console.log("\n=== E2E TEST: Dashboard Flow ===");
    
    // Arrange - Initialiser les mocks
    const api = new MockCrowdSecApiService();
    const cli = new MockCrowdCliService(api);
    const geo = new MockGeoIpService();

    // Act - Rafraîchir le tableau de bord
    console.log("1. Refresh dashboard stats...");
    const stats = await api.getSnapshotStatsAsync();
    
    console.log("2. Get recent decisions...");
    const decisions = await api.getDecisionsAsync();
    
    console.log("3. Check GeoIP availability...");
    const geoOk = geo.isDatabaseAvailable;

    // Assert - Valider les résultats
    assert(stats.totalAlerts > 0, "Stats should have total alerts");
    assert(decisions.length > 0, "Should have recent decisions");
    assert(geoOk === true, "GeoIP should be available in mock");

    console.log("✅ Dashboard flow completed successfully");
    return { stats, decisions, geoOk };
}

async function e2eDecisionManagementFlow() {
    console.log("\n=== E2E TEST: Decision Management Flow ===");
    
    const api = new MockCrowdSecApiService();
    const cli = new MockCrowdCliService(api);
    
    // Act - Ajouter une décision
    console.log("1. Add decision for test IP...");
    await cli.addDecisionAsync("192.168.1.100", "4h", "test-ban", "ban");
    
    // Act - Lister les décisions
    console.log("2. List all decisions...");
    const decisions = await api.getDecisionsAsync();
    
    // Assert
    assert(decisions.length === 3, `Should have 3 decisions after adding 1 (now have ${decisions.length})`);
    
    // Act - Supprimer la décision test
    console.log("3. Delete test decision...");
    await cli.deleteDecisionAsync(4);
    
    // Assert
    const remainingDecisions = await api.getDecisionsAsync();
    assert(remainingDecisions.length === 2, `Should have 2 decisions after deletion (now have ${remainingDecisions.length})`);

    console.log("✅ Decision management flow completed successfully");
    return { initial: 3, final: 2 };
}

async function e2eAlertWorkflow() {
    console.log("\n=== E2E TEST: Alert Workflow ===");
    
    const api = new MockCrowdSecApiService();
    const cli = new MockCrowdCliService(api);
    
    // Act - Lister des alertes avec filtres
    console.log("1. List alerts with IP filter...");
    const alerts = await cli.listAlertsAsync(10, "192.168.1.100", null, null, null, null);
    
    // Assert
    assert(alerts.length > 0, "Should have filtered alerts");
    
    // Act - Banir une source d'alerte
    console.log("2. Ban alert source...");
    const banResult = await cli.addDecisionAsync(
        "192.168.1.100", 
        "4h", 
        "Alert ban from malicious activity", 
        "ban"
    );
    
    // Assert
    assert(banResult.success === true, "Ban should succeed");

    console.log("✅ Alert workflow completed successfully");
    return { alertsCount: alerts.length };
}

async function e2eBouncerPruneFlow() {
    console.log("\n=== E2E TEST: Bouncer Prune Flow ===");
    
    const cli = new MockCrowdCliService(null); // API pas nécessaire ici
    
    // Act - Pruner les bouncers inactifs
    console.log("1. Prune inactive bouncers (threshold 1h)...");
    const result = await cli.pruneBouncersAsync("1h", true);
    
    // Assert
    assert(result.success === true, "Prune should succeed");
    assert(result.pruned > 0, `Should have pruned some bouncers (pruned: ${result.pruned})`);

    console.log("✅ Bouncer prune flow completed successfully");
    return result;
}

async function e2eGeoIpNormalizationFlow() {
    console.log("\n=== E2E TEST: GeoIP Normalization Flow ===");
    
    const geo = new MockGeoIpService();
    
    // Act - Normaliser plusieurs adresses
    console.log("1. Normalize IPv4 addresses...");
    const ipv4Result = geo.normalizeAddress("::ffff:192.168.1.1");
    
    console.log("2. Check GeoIP availability...");
    const available = geo.isDatabaseAvailable;

    // Assert
    assert(ipv4Result === "192.168.1.1", `IPv6-mapped IPv4 should normalize (${ipv4Result})`);
    assert(available === true, "GeoIP database should be available");

    console.log("✅ GeoIP normalization flow completed successfully");
    return { ipv4: ipv4Result, geoOk: available };
}

async function e2eEventBusSnapshotFlow() {
    console.log("\n=== E2E TEST: Event Bus Snapshot Flow ===");
    
    const bus = new MockEventBus();
    
    // Act - Ajouter des événements
    console.log("1. Add threat events to bus...");
    for (let i = 0; i < 10; i++) {
        await bus.addEvent({ sourceIp: `203.0.113.${i % 256}`, severity: "High" });
    }
    
    // Act - Obtenir le snapshot
    console.log("2. Get snapshot with latest events...");
    const snapshot = bus.getSnapshot();
    
    // Assert
    assert(snapshot.stats.totalAlerts === 10, `Should have 10 alerts (now have ${snapshot.stats.totalAlerts})`);
    assert(snapshot.recentEvents.length === 10, "Snapshot should include all recent events");

    console.log("✅ Event bus snapshot flow completed successfully");
    return snapshot;
}

async function e2eFullApplicationWorkflow() {
    console.log("\n=== E2E TEST: Full Application Workflow ===");
    
    // Initialize all mocks
    const api = new MockCrowdSecApiService();
    const cli = new MockCrowdCliService(api);
    const geo = new MockGeoIpService();
    const bus = new MockEventBus();
    
    let totalSteps = 0;
    let passedSteps = 0;
    
    // Step 1: Dashboard refresh
    try {
        console.log(`\n[${++totalSteps}] Refresh dashboard...`);
        await e2eDashboardFlow();
        passedSteps++;
    } catch (error) {
        console.error(`❌ Dashboard flow failed: ${error.message}`);
    }
    
    // Step 2: Decision management
    try {
        console.log(`\n[${++totalSteps}] Decision CRUD...`);
        await e2eDecisionManagementFlow();
        passedSteps++;
    } catch (error) {
        console.error(`❌ Decision flow failed: ${error.message}`);
    }
    
    // Step 3: Alert workflow
    try {
        console.log(`\n[${++totalSteps}] Alert handling...`);
        await e2eAlertWorkflow();
        passedSteps++;
    } catch (error) {
        console.error(`❌ Alert flow failed: ${error.message}`);
    }
    
    // Step 4: Bouncer prune
    try {
        console.log(`\n[${++totalSteps}] Bouncer prune...`);
        await e2eBouncerPruneFlow();
        passedSteps++;
    } catch (error) {
        console.error(`❌ Prune flow failed: ${error.message}`);
    }
    
    // Step 5: GeoIP normalization
    try {
        console.log(`\n[${++totalSteps}] GeoIP normalization...`);
        await e2eGeoIpNormalizationFlow();
        passedSteps++;
    } catch (error) {
        console.error(`❌ GeoIP flow failed: ${error.message}`);
    }
    
    // Step 6: Event bus
    try {
        console.log(`\n[${++totalSteps}] Event bus snapshot...`);
        await e2eEventBusSnapshotFlow();
        passedSteps++;
    } catch (error) {
        console.error(`❌ Event bus flow failed: ${error.message}`);
    }
    
    // Final assertion
    assert(passedSteps === 6, `All 6 flows should pass (passed: ${passedSteps})`);
    
    console.log(`\n✅ Full application workflow completed with ${passedSteps}/${totalSteps} steps passed`);
    return { totalSteps, passedSteps };
}

// Export pour xunit integration
async function runE2EMocksTests() {
    await e2eDashboardFlow();
    await e2eDecisionManagementFlow();
    await e2eAlertWorkflow();
    await e2eBouncerPruneFlow();
    await e2eGeoIpNormalizationFlow();
    await e2eEventBusSnapshotFlow();
    
    return "6 E2E mock tests passed successfully";
}

export { 
    MockCrowdSecApiService, 
    MockCrowdCliService, 
    MockGeoIpService,
    MockEventBus,
    runE2EMocksTests
};
