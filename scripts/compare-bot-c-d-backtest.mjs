#!/usr/bin/env node

import http from 'node:http';

const BOT_KEYS = ['C2026', 'D2026', 'E2026', 'F2026'];

const args = Object.fromEntries(
    process.argv.slice(2).map(argument => {
        const [key, ...value] = argument.replace(/^--/, '').split('=');
        return [key, value.join('=') || 'true'];
    }));

const dateFrom = args.from ?? '2026-08-08';
const dateTo = args.to ?? new Date().toISOString().slice(0, 10);
const baseUrl = (args.url ?? process.env.CORNERS_API_URL ?? 'http://localhost:5070').replace(/\/$/, '');
const apiKey = process.env.CORNERS_INTERNAL_API_KEY;
if (!apiKey) {
    throw new Error('CORNERS_INTERNAL_API_KEY is required.');
}
const endpoint = new URL('/api/automated-corners/selections', baseUrl);
endpoint.searchParams.set('dateFrom', dateFrom);
endpoint.searchParams.set('dateTo', dateTo);

const selections = await getJson(endpoint, apiKey);
const configurationFilters = {
    C2026: args['c-configuration-version'] ?? null,
    D2026: args['d-configuration-version'] ?? null,
    E2026: args['e-configuration-version'] ?? null
    ,F2026: args['f-configuration-version'] ?? null
};
const comparable = selections
    .map(selection => ({ selection, botKey: readBotKey(selection) }))
    .filter(row => BOT_KEYS.includes(row.botKey))
    .filter(row => !configurationFilters[row.botKey]
        || readConfigurationVersion(row.selection) === configurationFilters[row.botKey]);

const output = {
    generatedAtUtc: new Date().toISOString(),
    temporalPolicy: {
        dateFrom,
        dateTo,
        reason: 'Los modelos base declaran TrainedThrough=2026-08-07; solo se comparan partidos posteriores.'
    },
    configurationFilters,
    overall: Object.fromEntries(BOT_KEYS.map(botKey => [
        botKey,
        summarize(comparable.filter(row => row.botKey === botKey).map(row => row.selection))
    ])),
    byMarketFamily: compareGroups(comparable, row => marketFamily(row.selection)),
    byMarketScope: compareGroups(comparable, row => marketScope(row.selection)),
    byMarketAndScope: compareGroups(
        comparable,
        row => `${marketFamily(row.selection)}:${marketScope(row.selection)}`),
    byExperimentVersion: summarizeExperimentVersions(comparable),
    pairedExperiment: summarizePairs(comparable),
    pairedExperiments: {
        C2026_vs_D2026: summarizePair(comparable, 'C2026', 'D2026'),
        C2026_vs_E2026: summarizePair(comparable, 'C2026', 'E2026'),
        D2026_vs_E2026: summarizePair(comparable, 'D2026', 'E2026')
        ,E2026_vs_F2026: summarizePair(comparable, 'E2026', 'F2026')
        ,C2026_vs_F2026: summarizePair(comparable, 'C2026', 'F2026')
    },
    botDStrength: summarizeStrength(
        comparable.filter(row => row.botKey === 'D2026').map(row => row.selection)),
    botEEmpiricalCalibration: summarizeEmpiricalCalibration(
        comparable.filter(row => row.botKey === 'E2026').map(row => row.selection)),
    interpretation: [
        'Accuracy excludes Pending, Push and Void; half wins/losses retain their settled Status but are reflected exactly in profit and yield.',
        'Yield is total ProfitLoss divided by the real settled Stake, so Asian quarter-lines remain economically correct.',
        'C, D and E must be compared on the same match/market pair; the pairwise blocks separate selection coverage from result quality.',
        'Do not tune Bot D or Bot E from a handful of resolved picks. For E, inspect effective sample, evidence tier and reliability before drawing conclusions.'
    ]
};

process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);

function compareGroups(rows, keySelector) {
    const keys = [...new Set(rows.map(keySelector))].sort();
    return Object.fromEntries(keys.map(key => [key, Object.fromEntries(BOT_KEYS.map(botKey => [
        botKey,
        summarize(rows
            .filter(row => row.botKey === botKey && keySelector(row) === key)
            .map(row => row.selection))
    ]))]));
}

function summarizeExperimentVersions(rows) {
    const groups = new Map();
    for (const row of rows) {
        const configurationVersion = readConfigurationVersion(row.selection) || 'unknown';
        const automationVersion = String(
            row.selection.AutomationVersion ?? row.selection.automationVersion ?? 'unknown');
        const key = `${row.botKey}|${configurationVersion}|${automationVersion}`;
        if (!groups.has(key)) {
            groups.set(key, {
                botKey: row.botKey,
                configurationVersion,
                automationVersion,
                selections: []
            });
        }
        groups.get(key).selections.push(row.selection);
    }

    return [...groups.values()]
        .sort((left, right) => left.botKey.localeCompare(right.botKey)
            || left.configurationVersion.localeCompare(right.configurationVersion)
            || left.automationVersion.localeCompare(right.automationVersion))
        .map(group => ({
            botKey: group.botKey,
            configurationVersion: group.configurationVersion,
            automationVersion: group.automationVersion,
            ...summarize(group.selections)
        }));
}

function summarize(rows) {
    const statusCounts = Object.fromEntries(
        ['Pending', 'Won', 'Lost', 'Push', 'Void'].map(status => [
            status,
            rows.filter(row => String(row.Status ?? row.status) === status).length
        ]));
    const resolved = rows.filter(row => ['Won', 'Lost', 'Push'].includes(String(row.Status ?? row.status)));
    const decisions = resolved.filter(row => ['Won', 'Lost'].includes(String(row.Status ?? row.status)));
    const totalStake = sum(resolved, row => number(row.Stake ?? row.stake));
    const profitLoss = sum(resolved, row => number(row.ProfitLoss ?? row.profitLoss));
    const won = statusCounts.Won;
    const average = (items, selector) => items.length === 0 ? null : round(sum(items, selector) / items.length, 4);
    return {
        picks: rows.length,
        ...statusCounts,
        resolved: resolved.length,
        resolutionCoveragePct: percent(resolved.length, rows.length),
        accuracyPct: percent(won, decisions.length),
        profitLoss: round(profitLoss, 3),
        settledStake: round(totalStake, 3),
        yieldPct: percent(profitLoss, totalStake),
        averageOdds: average(rows, row => number(row.Odds ?? row.odds)),
        averageEdgePct: scalePercent(average(rows, row => number(row.ProbabilityEdge ?? row.probabilityEdge))),
        averageExpectedValuePct: scalePercent(average(rows, row => number(row.ExpectedValue ?? row.expectedValue)))
    };
}

function summarizeStrength(rows) {
    const snapshots = rows
        .map(readDecisionReason)
        .map(reason => reason?.featureSnapshot?.teamStrength)
        .filter(Boolean);
    const available = snapshots.filter(snapshot => snapshot.result?.isAvailable ?? snapshot.result?.IsAvailable);
    const read = (snapshot, camel, pascal) => number(snapshot.result?.[camel] ?? snapshot.result?.[pascal]);
    const average = (items, selector) => items.length === 0 ? null : round(sum(items, selector) / items.length, 4);
    return {
        publishedPicksWithSnapshot: snapshots.length,
        availableStrengthSnapshots: available.length,
        averageAdjustedGap: average(available, snapshot => read(snapshot, 'adjustedStrengthGap', 'AdjustedStrengthGap')),
        averageConfidence: average(available, snapshot => read(snapshot, 'confidenceScore', 'ConfidenceScore')),
        averageDirectMeetings: average(available, snapshot => read(snapshot, 'directMatches', 'DirectMatches')),
        averageCommonOpponents: average(available, snapshot => read(snapshot, 'commonOpponents', 'CommonOpponents')),
        averageProbabilityAdjustmentPct: scalePercent(average(
            snapshots,
            snapshot => number(snapshot.probabilityAdjustment ?? snapshot.ProbabilityAdjustment))),
        averageAbsoluteProbabilityAdjustmentPct: scalePercent(average(
            snapshots,
            snapshot => Math.abs(number(snapshot.probabilityAdjustment ?? snapshot.ProbabilityAdjustment)))),
        largestAbsoluteProbabilityAdjustmentPct: scalePercent(
            snapshots.length === 0
                ? null
                : Math.max(...snapshots.map(snapshot =>
                    Math.abs(number(snapshot.probabilityAdjustment ?? snapshot.ProbabilityAdjustment)))))
    };
}

function summarizeEmpiricalCalibration(rows) {
    const snapshots = rows
        .map(readDecisionReason)
        .map(reason => reason?.featureSnapshot?.empiricalCalibration
            ?? reason?.FeatureSnapshot?.EmpiricalCalibration)
        .filter(Boolean);
    const resultOf = snapshot => snapshot.result ?? snapshot.Result ?? {};
    const available = snapshots.filter(snapshot =>
        Boolean(resultOf(snapshot).isAvailable ?? resultOf(snapshot).IsAvailable));
    const read = (snapshot, camel, pascal) => numberOrNull(
        resultOf(snapshot)?.[camel] ?? resultOf(snapshot)?.[pascal]);
    const average = (items, selector) => {
        const values = items.map(selector).filter(value => value !== null);
        return values.length === 0 ? null : round(values.reduce((sum, value) => sum + value, 0) / values.length, 4);
    };
    const evidenceTiers = available.reduce((counts, snapshot) => {
        const result = resultOf(snapshot);
        const tier = String(result.evidenceTier ?? result.EvidenceTier ?? 'Unknown');
        counts[tier] = (counts[tier] ?? 0) + 1;
        return counts;
    }, {});
    const evidenceHashes = new Set(available
        .map(snapshot => {
            const result = resultOf(snapshot);
            return String(result.evidenceHash ?? result.EvidenceHash ?? '');
        })
        .filter(Boolean));

    return {
        publishedPicksWithSnapshot: snapshots.length,
        availableCalibrationSnapshots: available.length,
        unavailableCalibrationSnapshots: snapshots.length - available.length,
        evidenceTiers,
        uniqueEvidenceHashes: evidenceHashes.size,
        averageSelectedFixtures: average(available, snapshot =>
            read(snapshot, 'selectedFixtures', 'SelectedFixtures')),
        averageEffectiveSampleSize: average(available, snapshot =>
            read(snapshot, 'effectiveSampleSize', 'EffectiveSampleSize')),
        averageReliability: average(available, snapshot =>
            read(snapshot, 'reliability', 'Reliability')),
        averageConservativeExpectedValuePct: scalePercent(average(available, snapshot =>
            read(snapshot, 'conservativeExpectedValue', 'ConservativeExpectedValue'))),
        averageConservativeEquivalentProbabilityPct: scalePercent(average(available, snapshot =>
            read(snapshot, 'conservativeEquivalentProbability', 'ConservativeEquivalentProbability')))
    };
}

function summarizePairs(rows) {
    const cByKey = new Map(rows
        .filter(row => row.botKey === 'C2026')
        .map(row => [comparisonKey(row.selection), row.selection]));
    const dByKey = new Map(rows
        .filter(row => row.botKey === 'D2026')
        .map(row => [comparisonKey(row.selection), row.selection]));
    const pairedKeys = [...dByKey.keys()].filter(key => cByKey.has(key));
    const pairs = pairedKeys.map(key => ({ c: cByKey.get(key), d: dByKey.get(key) }));
    const onlyC = [...cByKey.entries()].filter(([key]) => !dByKey.has(key)).map(([, value]) => value);
    const onlyD = [...dByKey.entries()].filter(([key]) => !cByKey.has(key)).map(([, value]) => value);
    const resolvedPairs = pairs.filter(pair =>
        ['Won', 'Lost', 'Push'].includes(String(pair.c.Status ?? pair.c.status))
        && ['Won', 'Lost', 'Push'].includes(String(pair.d.Status ?? pair.d.status)));
    return {
        sameMatchAndMarket: pairs.length,
        onlyBotC: summarize(onlyC),
        onlyBotD: summarize(onlyD),
        changedSide: pairs.filter(pair => selectedSide(pair.c) !== selectedSide(pair.d)).length,
        changedLine: pairs.filter(pair => number(pair.c.LineValue ?? pair.c.lineValue)
            !== number(pair.d.LineValue ?? pair.d.lineValue)).length,
        identicalPick: pairs.filter(pair => selectedSide(pair.c) === selectedSide(pair.d)
            && number(pair.c.LineValue ?? pair.c.lineValue) === number(pair.d.LineValue ?? pair.d.lineValue)).length,
        bothResolved: resolvedPairs.length,
        botDProfitMinusBotC: round(sum(resolvedPairs, pair =>
            number(pair.d.ProfitLoss ?? pair.d.profitLoss)
            - number(pair.c.ProfitLoss ?? pair.c.profitLoss)), 3)
    };
}

function summarizePair(rows, leftKey, rightKey) {
    const leftByKey = new Map(rows
        .filter(row => row.botKey === leftKey)
        .map(row => [comparisonKey(row.selection), row.selection]));
    const rightByKey = new Map(rows
        .filter(row => row.botKey === rightKey)
        .map(row => [comparisonKey(row.selection), row.selection]));
    const pairedKeys = [...rightByKey.keys()].filter(key => leftByKey.has(key));
    const pairs = pairedKeys.map(key => ({
        left: leftByKey.get(key),
        right: rightByKey.get(key)
    }));
    const onlyLeft = [...leftByKey.entries()]
        .filter(([key]) => !rightByKey.has(key)).map(([, value]) => value);
    const onlyRight = [...rightByKey.entries()]
        .filter(([key]) => !leftByKey.has(key)).map(([, value]) => value);
    const resolvedPairs = pairs.filter(pair =>
        ['Won', 'Lost', 'Push'].includes(String(pair.left.Status ?? pair.left.status))
        && ['Won', 'Lost', 'Push'].includes(String(pair.right.Status ?? pair.right.status)));
    return {
        leftBot: leftKey,
        rightBot: rightKey,
        sameMatchAndMarket: pairs.length,
        onlyLeft: summarize(onlyLeft),
        onlyRight: summarize(onlyRight),
        changedSide: pairs.filter(pair => selectedSide(pair.left) !== selectedSide(pair.right)).length,
        changedLine: pairs.filter(pair => number(pair.left.LineValue ?? pair.left.lineValue)
            !== number(pair.right.LineValue ?? pair.right.lineValue)).length,
        identicalPick: pairs.filter(pair => selectedSide(pair.left) === selectedSide(pair.right)
            && number(pair.left.LineValue ?? pair.left.lineValue)
                === number(pair.right.LineValue ?? pair.right.lineValue)).length,
        bothResolved: resolvedPairs.length,
        rightProfitMinusLeft: round(sum(resolvedPairs, pair =>
            number(pair.right.ProfitLoss ?? pair.right.profitLoss)
            - number(pair.left.ProfitLoss ?? pair.left.profitLoss)), 3)
    };
}

function comparisonKey(selection) {
    const source = String(selection.Source ?? selection.source ?? '').toUpperCase();
    const matchId = selection.SourceMatchId ?? selection.sourceMatchId;
    const fallback = [
        String(selection.MatchDate ?? selection.matchDate ?? '').slice(0, 16),
        selection.StandardizedHomeTeam ?? selection.standardizedHomeTeam ?? selection.HomeTeam ?? selection.homeTeam,
        selection.StandardizedAwayTeam ?? selection.standardizedAwayTeam ?? selection.AwayTeam ?? selection.awayTeam
    ].join('|').toUpperCase();
    return `${source}|${matchId || fallback}|${String(selection.MarketType ?? selection.marketType ?? '').toUpperCase()}`;
}

function selectedSide(selection) {
    return String(selection.SelectedSide ?? selection.selectedSide ?? '').toUpperCase();
}

function readBotKey(selection) {
    const reason = readDecisionReason(selection);
    const explicit = String(reason?.botProfile ?? reason?.BotProfile ?? '').toUpperCase();
    if (explicit === 'C' || explicit === 'C2026') return 'C2026';
    if (explicit === 'D' || explicit === 'D2026') return 'D2026';
    if (explicit === 'E' || explicit === 'E2026') return 'E2026';
    if (explicit === 'F' || explicit === 'F2026') return 'F2026';
    const version = String(selection.AutomationVersion ?? selection.automationVersion ?? '').toUpperCase();
    if (version.endsWith('-C2026')) return 'C2026';
    if (version.endsWith('-D2026')) return 'D2026';
    if (version.endsWith('-E2026')) return 'E2026';
    if (version.endsWith('-F2026')) return 'F2026';
    return explicit || 'UNKNOWN';
}

function readDecisionReason(selection) {
    try {
        const value = selection.DecisionReason ?? selection.decisionReason;
        return typeof value === 'string' ? JSON.parse(value) : value;
    } catch {
        return null;
    }
}

function readConfigurationVersion(selection) {
    const reason = readDecisionReason(selection);
    return String(
        reason?.configurationVersion
        ?? reason?.ConfigurationVersion
        ?? reason?.featureSnapshot?.configurationVersion
        ?? reason?.FeatureSnapshot?.ConfigurationVersion
        ?? '').trim();
}

function marketFamily(selection) {
    const market = String(selection.MarketType ?? selection.marketType ?? '').toUpperCase();
    if (market.includes('SHOTSONGOAL')) return 'SOG';
    if (market.includes('SHOTS')) return 'SHOTS';
    if (market.includes('GOALS')) return 'GOALS';
    if (market.includes('CORNERS')) return 'CORNERS';
    return 'OTHER';
}

function marketScope(selection) {
    const market = String(selection.MarketType ?? selection.marketType ?? '').toUpperCase();
    if (market.startsWith('HOMETEAM')) return 'HOME';
    if (market.startsWith('AWAYTEAM')) return 'AWAY';
    return 'TOTAL';
}

function sum(rows, selector) {
    return rows.reduce((total, row) => total + selector(row), 0);
}

function number(value) {
    const result = Number(value);
    return Number.isFinite(result) ? result : 0;
}

function numberOrNull(value) {
    const result = Number(value);
    return Number.isFinite(result) ? result : null;
}

function round(value, digits) {
    const factor = 10 ** digits;
    return Math.round((value + Number.EPSILON) * factor) / factor;
}

function percent(value, denominator) {
    return denominator === 0 ? null : round(value / denominator * 100, 2);
}

function scalePercent(value) {
    return value === null ? null : round(value * 100, 2);
}

function getJson(url, key) {
    return new Promise((resolve, reject) => {
        const request = http.get(url, { headers: { 'X-Internal-Api-Key': key } }, response => {
            response.setEncoding('utf8');
            let body = '';
            response.on('data', chunk => { body += chunk; });
            response.on('end', () => {
                if ((response.statusCode ?? 500) >= 400) {
                    reject(new Error(`Request failed (${response.statusCode}): ${body}`));
                    return;
                }
                try {
                    resolve(JSON.parse(body));
                } catch (error) {
                    reject(error);
                }
            });
        });
        request.on('error', reject);
    });
}
