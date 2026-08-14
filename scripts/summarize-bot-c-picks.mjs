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
const requestedStatus = String(args.status ?? 'all').trim();
const baseUrl = (args.url ?? process.env.CORNERS_API_URL ?? 'http://localhost:5070').replace(/\/$/, '');
const apiKey = process.env.CORNERS_INTERNAL_API_KEY?.trim();
if (!apiKey) {
    throw new Error('Set CORNERS_INTERNAL_API_KEY before requesting selections.');
}
const endpoint = new URL('/api/automated-corners/selections', baseUrl);
endpoint.searchParams.set('dateFrom', dateFrom);
endpoint.searchParams.set('dateTo', dateTo);
if (requestedStatus.toLowerCase() !== 'all') {
    endpoint.searchParams.set('status', requestedStatus);
}

const selections = await new Promise((resolve, reject) => {
    const request = http.get(endpoint, {
        headers: { 'X-Internal-Api-Key': apiKey }
    }, response => {
        response.setEncoding('utf8');
        let responseBody = '';
        response.on('data', chunk => {
            responseBody += chunk;
        });
        response.on('end', () => {
            if ((response.statusCode ?? 500) >= 400) {
                reject(new Error(`Selections failed (${response.statusCode}): ${responseBody}`));
                return;
            }
            try {
                resolve(JSON.parse(responseBody));
            } catch (error) {
                reject(error);
            }
        });
    });
    request.on('error', reject);
});

const botC = selections.filter(selection =>
    String(selection.AutomationVersion ?? selection.automationVersion ?? '').includes('C2026'));
const value = (selection, pascalName, camelName) => selection[pascalName] ?? selection[camelName];
const marketType = selection => selection.MarketType ?? selection.marketType ?? 'Unknown';
const matchDate = selection => selection.MatchDate ?? selection.matchDate;
const selectionStatus = selection => value(selection, 'Status', 'status') ?? 'Unknown';
const byMarket = Object.entries(Object.groupBy(botC, marketType))
    .map(([market, rows]) => ({ market, count: rows.length }))
    .sort((left, right) => left.market.localeCompare(right.market));
const byStatus = Object.entries(Object.groupBy(botC, selectionStatus))
    .map(([status, rows]) => ({ status, count: rows.length }))
    .sort((left, right) => left.status.localeCompare(right.status));
const dates = botC.map(matchDate).filter(Boolean).sort();
const identityKey = selection => {
    const sourceMatchId = String(value(selection, 'SourceMatchId', 'sourceMatchId') ?? '').trim();
    const sourceUrl = String(value(selection, 'SourceUrl', 'sourceUrl') ?? '').trim();
    const matchIdentity = sourceMatchId
        ? `ID|${sourceMatchId}`
        : sourceUrl
            ? `URL|${sourceUrl}`
            : [
                'MATCH',
                matchDate(selection),
                value(selection, 'StandardizedLeague', 'standardizedLeague')
                    ?? value(selection, 'League', 'league'),
                value(selection, 'StandardizedHomeTeam', 'standardizedHomeTeam')
                    ?? value(selection, 'HomeTeam', 'homeTeam'),
                value(selection, 'StandardizedAwayTeam', 'standardizedAwayTeam')
                    ?? value(selection, 'AwayTeam', 'awayTeam')
            ].join('|');
    return [
        value(selection, 'AutomationVersion', 'automationVersion'),
        value(selection, 'Source', 'source'),
        marketType(selection),
        matchIdentity
    ].join('|');
};
const duplicateGroups = Object.entries(Object.groupBy(botC, identityKey))
    .filter(([, rows]) => rows.length > 1)
    .map(([key, rows]) => ({ key, count: rows.length }));

process.stdout.write(`${JSON.stringify({
    status: 'ok',
    dateFrom,
    dateTo,
    requestedStatus,
    botCSelections: botC.length,
    firstMatch: dates[0] ?? null,
    lastMatch: dates.at(-1) ?? null,
    byStatus,
    byMarket,
    duplicateGroupCount: duplicateGroups.length,
    duplicateGroups: duplicateGroups.slice(0, 10)
}, null, 2)}\n`);
