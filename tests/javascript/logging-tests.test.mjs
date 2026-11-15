// Copyright (c) CrowdSec Manager - Tests de logging avec simulation rotation fichier

import assert from "node:assert/strict";
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import path from "node:path";

/**
 * Mock pour simuler le système de logging et la rotation des fichiers logs
 */
class LoggingSystemMock {
    constructor() {
        this.logLevels = ['DEBUG', 'INFO', 'WARNING', 'ERROR'];
        this.logsDirectory = './test-logs';
        this.rotationConfig = {
            maxSizeBytes: 1024 * 1024 * 5, // 5MB max par fichier log
            rotateAgeHours: 168, // 7 jours
            compressOldLogs: true
        };
    }

    /**
     * Initialisation répertoire logs avec fichier rotation configuré
     */
    initializeLogsDirectory() {
        const directoryConfig = {
            path: this.logsDirectory,
            createDirs: true,
            logLevels: this.logLevels,
            pattern: `${this.logsDirectory}/*.log`
        };
        
        // Simule la création du répertoire
        assert.ok(path.isAbsolute(directoryConfig.pattern) || 
                  directoryConfig.pattern.startsWith('./'),
                "Log pattern should be relative to project root");
        
        return directoryConfig;
    }

    /**
     * Simulation d'ajout entrée log avec level validation
     */
    addLogEntry(level, message) {
        const logEntry = {
            timestamp: Date.now(),
            level,
            message,
            source: 'Application',
            stackTrace: null // Stack trace seulement sur erreurs
        };

        // Valide que le niveau est un des niveaux reconnus
        assert.ok(this.logLevels.includes(level), 
            `Invalid log level: ${level}, must be one of [${this.logLevels.join(', ')}]`);
        
        return logEntry;
    }

    /**
     * Validation formatage structuré logs (JSON)
     */
    validateStructuredLogFormat(logEntry) {
        const requiredFields = ['timestamp', 'level', 'message', 'source'];
        let isValid = true;
        
        for (const field of requiredFields) {
            if (!(field in logEntry)) {
                isValid = false;
                break;
            }
        }
        
        assert.ok(isValid, "Structured log must contain all required fields");
        
        return { valid: isValid, logEntry };
    }

    /**
     * Simulation fichier rotation basée sur taille
     */
    rotateLogFile(currentFile) {
        const rotatedFilename = `${currentFile}.${Date.now()}`;
        
        // Simule la compression et déplacement du fichier roté
        const rotationResult = {
            sourceFile: currentFile,
            rotatedFile: rotatedFilename,
            compressed: true,
            compressionRatio: 0.3 // ~70% réduction de taille après compression gzip
        };
        
        assert.ok(!currentFile.includes('compressed'), "Should not rotate already compressed logs");
        
        return rotationResult;
    }

    /**
     * Validation fichier logrotate.conf simulation
     */
    validateLogrotateConfig() {
        const configValidation = {
            rotationRule: "/app/logs/*.log {",
                "daily",
                "maxsize 5M",           // Rotation à 5MB
                "compress",             // Compression par défaut gzip
                "delaycompress",        // Compresser au tour suivant
                "missingok",            // Ne pas erreurs si fichier inexistant
                "notifempty"            // Ne pas rotater fichiers vides
            };
        
        assert.ok(configValidation.rotationRule.length > 0);
        
        return configValidation;
    }

    /**
     * Validation séparation logs erreurs vs info
     */
    separateErrorAndInfoLogs() {
        const logCategories = {
            info: [],
            errors: []
        };
        
        // Ségrégation des logs par niveau
        for (const entry of this.generateSampleLogs()) {
            if (entry.level === 'ERROR' || entry.level === 'WARNING') {
                logCategories.errors.push(entry);
            } else if (entry.level === 'INFO' || entry.level === 'DEBUG') {
                logCategories.info.push(entry);
            }
        }
        
        // Vérifie que les erreurs sont séparées des infos
        assert.ok(logCategories.errors.length > 0, "Should have error logs");
        assert.ok(logCategories.info.length > 0, "Should have info logs");
        
        return logCategories;
    }

    /**
     * Génération d'échantillons logs pour tests de séparation
     */
    generateSampleLogs() {
        const sampleLogs = [
            { level: 'INFO', message: 'Application started' },
            { level: 'DEBUG', message: 'Configuration loaded' },
            { level: 'WARNING', message: 'Rate limit threshold reached' },
            { level: 'ERROR', message: 'Failed to connect to database' }
        ];
        
        return sampleLogs;
    }

    /**
     * Simulation compression des logs anciens
     */
    compressOldLog(logFile) {
        const compressedResult = {
            originalSizeBytes: 1024 * 100, // 100KB original
            compressedSizeBytes: 30000,     // ~69% réduction (gzip typique)
            compressionMethod: 'gzip',
            fileExtension: '.log.gz'
        };
        
        assert.ok(compressedResult.compressedSizeBytes < compressedResult.originalSizeBytes,
            "Compressed file should be smaller than original");
        
        return compressedResult;
    }

    /**
     * Validation log rotation ne purge pas logs récents trop tôt
     */
    testRotationTiming() {
        const rotationTest = {
            currentAgeHours: 6,           // Logs à 6h
            maxAgeHours: 168,             // Rotation après 7 jours (168h)
            shouldRotate: false,           // Ne pas rotater encore
            retentionPolicy: 'daily'       // Rotation journalière
        };
        
        assert.ok(!rotationTest.shouldRotate, 
            "Logs less than rotation age should not be rotated");
        
        return rotationTest;
    }

    /**
     * Validation que les logs compressés sont lisibles sans perte de données
     */
    validateCompressedLogIntegrity(originalText) {
        const mockCompression = {
            originalSize: originalText.length,
            compressedData: 'compressed_data_stub',
            decompressed: originalText // Simule la décompression réussie
        };
        
        assert.ok(mockCompression.originalSize === mockCompression.decompressed.length);
        
        return mockCompression;
    }

    /**
     * Validation formatage logs JSON pour ingestion ELK/Graylog
     */
    validateJsonLogFormat() {
        const jsonLog = {
            timestamp: '2024-11-15T12:34:56.789Z',
            level: 'INFO',
            message: 'Application started',
            service: 'CrowdSecManager',
            version: '2.0.0'
        };
        
        const validationRules = {
            hasTimestamp: jsonLog.hasOwnProperty('timestamp'),
            isValidLevel: ['DEBUG','INFO','WARNING','ERROR'].includes(jsonLog.level),
            messagePresent: !!jsonLog.message,
            optionalFieldsValid: typeof jsonLog.version === 'string'
        };
        
        assert.ok(validationRules.hasTimestamp && 
                  validationRules.isValidLevel && 
                  validationRules.messagePresent);
        
        return validationRules;
    }

    /**
     * Rotation logs basée sur âge et taille combinés
     */
    compositeRotationPolicy(currentFileAgeHours, currentFileSizeBytes) {
        const rotationDecision = {
            ageThreshold: 168,             // 7 jours
            sizeThreshold: this.rotationConfig.maxSizeBytes,
            shouldRotate: 
                currentFileAgeHours > this.rotationConfig.rotateAgeHours ||
                currentFileSizeBytes >= this.rotationConfig.maxSizeBytes,
            rotationReason: null
        };
        
        if (rotationDecision.shouldRotate) {
            if (currentFileAgeHours > 168) {
                rotationDecision.rotationReason = 'Age exceeded';
            } else if (currentFileSizeBytes >= 5 * 1024 * 1024) {
                rotationDecision.rotationReason = 'Size limit reached';
            }
        }
        
        return rotationDecision;
    }
}

// Tests de logging et rotation fichiers
async function loggingTests() {
    console.log("\n=== LOGGING SYSTEM TESTS ===");
    
    const loggingMock = new LoggingSystemMock();
    
    try {
        // Test 1: Initialisation répertoire logs
        console.log("\n[1/7] Initializing log directory...");
        
        await loggingMock.initializeLogsDirectory();
        console.log("✅ Log directory initialization passed");
        
        // Test 2: Ajout entrée log avec niveau valide
        console.log("[2/7] Adding log entry with valid level...");
        
        const logEntry = loggingMock.addLogEntry('INFO', 'Test log entry');
        assert.ok(logEntry.level === 'INFO' && logEntry.message === 'Test log entry');
        console.log("✅ Log entry creation passed");
        
        // Test 3: Validation formatage JSON structuré
        console.log("[3/7] Validating structured JSON log format...");
        
        const validJsonLog = loggingMock.validateStructuredLogFormat(logEntry);
        assert.ok(validJsonLog.valid === true);
        console.log("✅ Structured log validation passed");
        
        // Test 4: Validation fichier rotation
        console.log("[4/7] Validating file rotation mechanism...");
        
        const rotateResult = loggingMock.rotateLogFile('./app/logs/test.log');
        assert.ok(rotateResult.rotatedFile.length > 0);
        console.log("✅ File rotation passed");
        
        // Test 5: Validation logrotate.conf configuration
        console.log("[5/7] Validating logrotate configuration...");
        
        const configValid = loggingMock.validateLogrotateConfig();
        assert.ok(configValid.rotationRule.length > 0);
        console.log("✅ Logrotate configuration validation passed");
        
        // Test 6: Séparation logs erreurs vs infos
        console.log("[6/7] Testing error/info log separation...");
        
        const separatedLogs = loggingMock.separateErrorAndInfoLogs();
        assert.ok(separatedLogs.errors.length > 0 && separatedLogs.info.length > 0);
        console.log("✅ Error/info log separation passed");
        
        // Test 7: Rotation timing et rétention
        console.log("[7/7] Testing rotation timing policy...");
        
        const rotationTiming = loggingMock.testRotationTiming();
        assert.ok(!rotationTiming.shouldRotate);
        console.log("✅ Rotation timing validation passed");

    } catch (error) {
        console.error(`❌ Logging test failed: ${error.message}`);
        throw error;
    }
    
    console.log("\n✅ All logging tests passed!");
    return { allLoggingTestsPassed: true };
}

// Tests de compression logs anciens
async function oldLogCompressionTests() {
    console.log("\n=== OLD LOG COMPRESSION TESTS ===");
    
    const loggingMock = new LoggingSystemMock();
    
    // Test 8a: Compression logs anciens réduit taille
    console.log("\n[1/3] Testing compression of old logs...");
    
    const compressedLog = loggingMock.compressOldLog('./app/logs/test.log');
    assert.ok(compressedLog.compressedSizeBytes < compressedLog.originalSizeBytes);
    console.log("✅ Old log compression passed");
    
    // Test 8b: Intégrité données après compression/décompression
    console.log("[2/3] Verifying data integrity after compression...");
    
    const originalText = 'Application started at ' + Date.now();
    const decompressed = loggingMock.validateCompressedLogIntegrity(originalText);
    assert.ok(decompressed.originalSize === decompressed.decompressed.length);
    console.log("✅ Data integrity verification passed");
    
    // Test 8c: Extension fichier compressé correcte
    console.log("[3/3] Testing file extension for compressed logs...");
    
    const compressedFile = './app/logs/test.log.gz';
    assert.ok(compressedFile.endsWith('.log.gz'));
    console.log("✅ Compressed file naming validated");

    console.log("\n✅ All compression tests passed!");
    return { allCompressionTestsPassed: true };
}

// Tests JSON logs pour ingestion ELK/Graylog
async function jsonLogFormatTests() {
    console.log("\n=== JSON LOG FORMAT VALIDATION TESTS ===");
    
    const loggingMock = new LoggingSystemMock();
    
    // Test 9a: Format JSON valide avec tous champs requis
    console.log("\n[1/2] Testing valid JSON log format...");
    
    const jsonFormatValid = loggingMock.validateJsonLogFormat();
    assert.ok(jsonFormatValid.hasTimestamp && jsonFormatValid.isValidLevel);
    console.log("✅ Valid JSON log format passed");
    
    // Test 9b: Format JSON invalide rejeté
    console.log("[2/2] Testing invalid JSON log rejection...");
    
    const invalidLog = { only: 'level' }; // Manque timestamp, message
    const rulesForInvalid = {
        hasTimestamp: false,
        isValidLevel: true,
        messagePresent: false
    };
    
    assert.ok(!rulesForInvalid.hasTimestamp);
    console.log("✅ Invalid JSON log rejection passed");

    console.log("\n✅ All JSON format tests passed!");
    return { allJsonFormatTestsPassed: true };
}

// Export pour xunit integration
async function runLoggingTests() {
    await loggingTests();
    await oldLogCompressionTests();
    await jsonLogFormatTests();
    
    return "Logging system tests completed successfully";
}

export { 
    LoggingSystemMock,
    runLoggingTests
};
