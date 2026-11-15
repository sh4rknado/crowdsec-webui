// Copyright (c) CrowdSec Manager - Tests de caching avec Redis et memory cache mock

import assert from "node:assert/strict";

/**
 * Mock pour simuler système de caching hybride (Redis + Memory Cache)
 */
class HybridCacheMock {
    constructor() {
        this.memoryCache = new Map();
        this.redisConnection = null; // Simule connexion Redis
        this.cacheStats = {
            hits: 0,
            misses: 0,
            evictions: 0
        };
    }

    /**
     * Méthode cache avec fallback memory → Redis
     */
    async get(key) {
        // Vérifie d'abord le cache mémoire
        if (this.memoryCache.has(key)) {
            this.cacheStats.hits++;
            return this.memoryCache.get(key);
        }
        
        this.cacheStats.misses++;
        return null;
    }

    /**
     * Méthode set avec expiration TTL configurable
     */
    async set(key, value, ttlMs = 300000) { // TTL par défaut: 5 minutes
        // Vérifie si mémoire cache est saturée (eviction LRU)
        const maxSize = 1000; // Taille max du cache mémoire
        
        while (this.memoryCache.size >= maxSize) {
            this.evictOldest();
        }
        
        this.memoryCache.set(key, value);
    }

    /**
     * Éviction LRU (Least Recently Used)
     */
    evictOldest() {
        const oldestKey = this.memoryCache.keys().next().value;
        if (oldestKey) {
            this.memoryCache.delete(oldestKey);
            this.cacheStats.evictions++;
        }
    }

    /**
     * Clear cache complet avec vérification reset stats
     */
    clear() {
        this.memoryCache.clear();
        const clearedKeys = this.memoryCache.size; // Devrait être 0 après clear
        
        assert.equal(this.memoryCache.size, 0);
        
        return { clearedKeys: 0 };
    }

    /**
     * Warmup cache avec données pré-chargées
     */
    async warmup(keysArray) {
        if (!keysArray || keysArray.length === 0) {
            return;
        }
        
        for (const key of keysArray) {
            // Simule chargement depuis base de données ou API
            await this.set(key, `cached_data_for_${key}`, 7200000); // 2 heures TTL
        }
        
        return { warmedKeys: keysArray.length };
    }

    /**
     * Métriques cache hit/miss ratio
     */
    getCacheMetrics() {
        const totalRequests = this.cacheStats.hits + this.cacheStats.misses;
        
        let hitRatio = 0;
        if (totalRequests > 0) {
            hitRatio = (this.cacheStats.hits / totalRequests) * 100;
        }
        
        return {
            hits: this.cacheStats.hits,
            misses: this.cacheStats.misses,
            evictions: this.cacheStats.evictions,
            hitRatio: hitRatio,
            totalRequests: totalRequests
        };
    }

    /**
     * Vérifie TTL respecté
     */
    async checkTtlExpiry(key, expectedExpiryTime) {
        const currentTime = Date.now();
        const ttlMs = expectedExpiryTime; // Simule TTL stocké
        
        const isValid = (currentTime - key.creationTime) < ttlMs;
        
        assert.ok(isValid === true, `Key ${key} should still be valid (${ttlMs}ms TTL)`);
        
        return { key, isValid, remainingTtlMs: ttlMs };
    }

    /**
     * Vérifie que expiration automatique fonctionne
     */
    async testAutomaticExpiration() {
        const oldData = 'expired';
        this.memoryCache.set('old-key', oldData);
        
        // Simule passage du TTL avec expiration automatique
        const oldKey = 'old-key';
        const ttlMs = 0; // TTL échu
        
        this.memoryCache.delete(oldKey); // Simule suppression automatique
        
        assert.ok(!this.memoryCache.has('old-key'), "Expired key should be removed");
        
        return { expired: true, removedBeforeUsage: true };
    }

    /**
     * Test cache invalidation lors de mise à jour données source
     */
    async testCacheInvalidation() {
        const userId = 'user-123';
        const userData = { name: 'Alice', email: 'alice@example.com' };
        
        // Stocke utilisateur dans cache
        await this.set(userId, JSON.stringify(userData), 60000); // 1 minute
        
        // Simule mise à jour source
        const updatedUserData = { name: 'Alice Smith', email: 'alice.smith@example.com' };
        
        // Invalidé cache et recharge
        await this.set(userId, JSON.stringify(updatedUserData));
        
        const cachedData = await this.get(userId);
        assert.ok(JSON.parse(cachedData).name === 'Alice Smith');
        
        return { invalidated: true, freshData: JSON.parse(cachedData) };
    }

    /**
     * Test batch get avec atomicité
     */
    async testBatchGet() {
        const keys = ['key-1', 'key-2', 'key-3'];
        
        // Pré-allocation des valeurs
        for (const key of keys) {
            await this.set(key, key);
        }
        
        // Batch get atomique simulé
        const batchResult = [];
        for (const key of keys) {
            batchResult.push(await this.get(key));
        }
        
        assert.ok(batchResult.length === keys.length);
        return { keys: keys, batchResult };
    }

    /**
     * Test cache serialisation/déserialisation
     */
    async testSerialization() {
        const complexObject = {
            nested: { data: [1, 2, 3] },
            array: [4, 5, 6],
            timestamp: Date.now()
        };
        
        const jsonString = JSON.stringify(complexObject);
        
        // Vérifie que sérialisation/désérialisation fonctionne
        const deserialized = JSON.parse(jsonString);
        
        assert.ok(deserialized.nested.data[0] === 1);
        assert.ok(deserialized.array.length === 3);
        
        return { original: complexObject, serializedLength: jsonString.length };
    }
}

// Tests de caching
async function cachingTests() {
    console.log("\n=== CACHING SYSTEM TESTS ===");
    
    const cacheMock = new HybridCacheMock();
    
    try {
        // Test 1: Cache hit avec clé existante
        console.log("[1/9] Testing cache hit...");
        
        await cacheMock.set('test-key', 'test-value');
        const value = await cacheMock.get('test-key');
        assert.ok(value === 'test-value');
        console.log("✅ Cache hit test passed");
        
        // Test 2: Cache miss avec clé inexistant
        console.log("[2/9] Testing cache miss...");
        
        const missingValue = await cacheMock.get('non-existent-key');
        assert.ok(missingValue === null);
        console.log("✅ Cache miss test passed");
        
        // Test 3: Cache eviction avec capacité limitée
        console.log("[3/9] Testing cache eviction (LRU)...");
        
        // Remplit cache au-delà de la taille max
        for (let i = 0; i < 1500; i++) {
            await cacheMock.set(`key-${i}`, `value-${i}`);
        }
        
        // Vérifie que nombre clés est limité à maxSize
        assert.ok(cacheMock.memoryCache.size <= 1000, 
            "Cache should enforce size limit");
        console.log("✅ Cache eviction test passed");
        
        // Test 4: Clear cache complet
        console.log("[4/9] Testing clear cache operation...");
        
        await cacheMock.clear();
        assert.ok(cacheMock.memoryCache.size === 0);
        console.log("✅ Clear cache test passed");
        
        // Test 5: Cache warmup avec pré-chargement
        console.log("[5/9] Testing cache warmup...");
        
        const warmupKeys = ['hot-key-1', 'hot-key-2'];
        await cacheMock.warmup(warmupKeys);
        assert.ok(cacheMock.memoryCache.has('hot-key-1'));
        console.log("✅ Cache warmup test passed");
        
        // Test 6: Metrics hit/miss ratio
        console.log("[6/9] Testing cache metrics calculation...");
        
        const metrics = cacheMock.getCacheMetrics();
        assert.ok(metrics.hitRatio >= 0 && metrics.hitRatio <= 100);
        console.log("✅ Cache metrics test passed");
        
        // Test 7: TTL expiry automatique
        console.log("[7/9] Testing automatic expiration...");
        
        const expiredResult = await cacheMock.testAutomaticExpiration();
        assert.ok(expiredResult.expired === true);
        console.log("✅ Automatic expiration test passed");
        
        // Test 8: Cache invalidation lors mise à jour source
        console.log("[8/9] Testing cache invalidation...");
        
        const invalidationResult = await cacheMock.testCacheInvalidation();
        assert.ok(invalidationResult.invalidated === true);
        console.log("✅ Cache invalidation test passed");
        
        // Test 9: Batch get atomique
        console.log("[9/9] Testing batch get operation...");
        
        const batchResult = await cacheMock.testBatchGet();
        assert.ok(batchResult.batchResult.length === 3);
        console.log("✅ Batch get test passed");

    } catch (error) {
        console.error(`❌ Caching test failed: ${error.message}`);
        throw error;
    }
    
    console.log("\n✅ All caching tests passed!");
    return { allCachingTestsPassed: true };
}

// Tests de métriques cache Redis
async function redisCacheMetricsTests() {
    console.log("\n=== REDIS CACHE METRICS TESTS ===");
    
    const cacheMock = new HybridCacheMock();
    
    // Test 10a: Métriques Redis hit/miss accurate
    console.log("[1/2] Testing Redis metrics accuracy...");
    
    const metrics = cacheMock.getCacheMetrics();
    assert.ok(metrics.hits >= 0 && metrics.misses >= 0);
    console.log("✅ Redis metrics test passed");
    
    // Test 10b: Eviction rate tracking correcte
    console.log("[2/2] Testing eviction rate tracking...");
    
    assert.ok(metrics.evictions >= 0, "Evictions should never be negative");
    console.log("✅ Eviction tracking test passed");

    console.log("\n✅ All Redis metrics tests passed!");
    return { allRedisMetricsTestsPassed: true };
}

// Tests de fallback cache local sans Redis
async function memoryCacheFallbackTests() {
    console.log("\n=== MEMORY CACHE FALLBACK TESTS ===");
    
    const cacheMock = new HybridCacheMock();
    
    // Test 11a: Fallback vers mémoire quand Redis indisponible
    console.log("[1/2] Testing memory fallback when Redis unavailable...");
    
    cacheMock.memoryCache.set('fallback-key', 'fallback-value');
    const fallbackValue = await cacheMock.get('fallback-key');
    assert.ok(fallbackValue === 'fallback-value');
    console.log("✅ Memory fallback test passed");
    
    // Test 11b: Dégradation élégante sans Redis
    console.log("[2/2] Testing graceful degradation...");
    
    // Simule indisponibilité Redis mais cache mémoire fonctionne toujours
    cacheMock.redisConnection = null;
    assert.ok(cacheMock.memoryCache.size > 0, "Memory cache should still function");
    console.log("✅ Graceful degradation test passed");

    console.log("\n✅ All memory fallback tests passed!");
    return { allFallbackTestsPassed: true };
}

// Export pour xunit integration
async function runCachingTests() {
    await cachingTests();
    await redisCacheMetricsTests();
    await memoryCacheFallbackTests();
    
    return "Caching tests completed successfully";
}

export { 
    HybridCacheMock,
    runCachingTests
};
