#!/usr/bin/env node

import http from 'node:http';

const args = Object.fromEntries(
    process.argv.slice(2).map(argument => {
        const [key, ...value] = argument.replace(/^--/, '').split('=');
        return [key, value.join('=') || 'true'];
    }));

const chileToday = () => {
    const parts = new Intl.DateTimeFormat('en-US', {
        timeZone: 'America/Santiago',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit'
    }).formatToParts(new Date());
    const part = type => parts.find(item => item.type === type)?.value;
    return `${part('year')}-${part('month')}-${part('day')}`;
};

const dateFrom = args.from ?? '2026-06-19';
const dateTo = args.to ?? chileToday();
const baseUrl = (args.url ?? process.env.CORNERS_API_URL ?? 'http://localhost:5070').replace(/\/$/, '');
const apiKey = process.env.CORNERS_INTERNAL_API_KEY?.trim();
if (!apiKey) {
    throw new Error('Set CORNERS_INTERNAL_API_KEY before running the backfill.');
}
const batchSize = Math.min(100, Math.max(1, Number(args['batch-size'] ?? 25)));
const startBatch = Math.max(1, Number(args['start-batch'] ?? 1));
const concurrency = Math.min(4, Math.max(1, Number(args.concurrency ?? 2)));
const knownTotalBatches = Math.max(0, Number(args['total-batches'] ?? 0));
const marketFamilies = args.markets ?? 'CORNERS,GOALS,SHOTS,SOG';
const botKeys = args.bots ?? 'C2026';
const normalizedBots = botKeys.split(',').map(value => value.trim().toUpperCase()).filter(Boolean);
if (normalizedBots.length === 0) {
    throw new Error('Use --bots=C2026,D2026,E2026,F2026 with at least one bot.');
}

if (!/^\d{4}-\d{2}-\d{2}$/.test(dateFrom) || !/^\d{4}-\d{2}-\d{2}$/.test(dateTo)) {
    throw new Error('Use --from=YYYY-MM-DD and --to=YYYY-MM-DD.');
}

const runBatch = async batchNumber => {
    const requestBody = JSON.stringify({
        dateFrom,
        dateTo,
        dryRun: false,
        excludeExistingSelections: false,
        batchNumber,
        batchSize,
        runBotC: normalizedBots.includes('C2026') || normalizedBots.includes('C'),
        onlyBotC: normalizedBots.length === 1 && (normalizedBots[0] === 'C2026' || normalizedBots[0] === 'C'),
        historicalBacktest: false,
        historicalBackfill: true,
        marketFamilies,
        botKeys: normalizedBots.join(',')
    });
    const endpoint = new URL('/api/automated-corners/run', baseUrl);
    if (endpoint.protocol !== 'http:') {
        throw new Error('The backfill runner currently supports a local http:// API URL.');
    }
    return await new Promise((resolve, reject) => {
        const request = http.request(endpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Content-Length': Buffer.byteLength(requestBody),
                'X-Internal-Api-Key': apiKey
            }
        }, response => {
            response.setEncoding('utf8');
            let responseBody = '';
            response.on('data', chunk => {
                responseBody += chunk;
            });
            response.on('end', () => {
                let body;
                try {
                    body = JSON.parse(responseBody);
                } catch {
                    body = { error: responseBody };
                }
                if ((response.statusCode ?? 500) >= 400) {
                    reject(new Error(
                        `Batch ${batchNumber} failed (${response.statusCode}): ${JSON.stringify(body)}`));
                    return;
                }
                resolve(body);
            });
        });
        request.on('error', reject);
        request.write(requestBody);
        request.end();
    });
};

let totalBatches = startBatch;
let inserted = 0;
let updated = 0;
let selected = 0;
let skipped = 0;
let errors = 0;

const consumeBatch = async batchNumber => {
    const result = await runBatch(batchNumber);
    inserted += Number(result.insertedRows ?? result.InsertedRows ?? 0);
    updated += Number(result.updatedRows ?? result.UpdatedRows ?? 0);
    selected += Number(result.selectedMatches ?? result.SelectedMatches ?? 0);
    skipped += Number(result.skippedMatches ?? result.SkippedMatches ?? 0);
    errors += Number(result.errorMatches ?? result.ErrorMatches ?? 0);

    process.stdout.write(`${JSON.stringify({
        batch: batchNumber,
        totalBatches,
        range: `${result.batchStart ?? result.BatchStart}-${result.batchEnd ?? result.BatchEnd}`,
        matches: result.totalMatches ?? result.TotalMatches,
        selections: result.selectedMatches ?? result.SelectedMatches,
        inserted: result.insertedRows ?? result.InsertedRows,
        updated: result.updatedRows ?? result.UpdatedRows,
        skipped: result.skippedMatches ?? result.SkippedMatches,
        errors: result.errorMatches ?? result.ErrorMatches
    })}\n`);

    return result;
};

let nextBatch;
if (knownTotalBatches >= startBatch) {
    totalBatches = knownTotalBatches;
    nextBatch = startBatch;
} else {
    const firstResult = await consumeBatch(startBatch);
    totalBatches = Number(firstResult.totalBatches ?? firstResult.TotalBatches ?? 0);
    nextBatch = startBatch + 1;
}
const worker = async () => {
    while (nextBatch <= totalBatches) {
        const batchNumber = nextBatch;
        nextBatch += 1;
        await consumeBatch(batchNumber);
    }
};

await Promise.all(Array.from(
    { length: Math.min(concurrency, Math.max(0, totalBatches - nextBatch + 1)) },
    worker));

process.stdout.write(`${JSON.stringify({
    status: 'complete',
    dateFrom,
    dateTo,
    marketFamilies,
    botKeys: normalizedBots,
    batches: totalBatches,
    concurrency,
    selected,
    inserted,
    updated,
    skipped,
    errors
}, null, 2)}\n`);
