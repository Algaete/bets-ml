import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const view = fs.readFileSync(path.join(root, 'CornersPrediction.Web/Views/BotPicks/Index.cshtml'), 'utf8');
const partial = fs.readFileSync(path.join(root, 'CornersPrediction.Web/Views/BotPicks/_Research.cshtml'), 'utf8');
const script = fs.readFileSync(path.join(root, 'CornersPrediction.Web/wwwroot/js/bot-picks-research.js'), 'utf8');
const inlineTemplate = view.match(/@section Scripts\s*{\s*<script>([\s\S]*?)<\/script>/)[1]
    .replace(/'@Url\.Action\("([^"]+)"[^\n]*?\)'/g, (_, action) => `'/BotPicks/${action}'`)
    .replace('@(canUpdateResults ? "true" : "false")', 'false')
    .replaceAll("'@language'", "'es'")
    .replaceAll("'@Model.Market.UnitLabel'", "'goles'");

class Element {
    constructor(id = '') {
        this.id = id;
        this.value = '';
        this.options = [];
        this.innerHTML = '';
        this.textContent = '';
        this.open = false;
        this.dataset = {};
        this.style = {};
        this.attributes = {};
        this.events = new Map();
        const classes = new Set();
        this.classList = {
            add: (...values) => values.forEach(value => classes.add(value)),
            remove: (...values) => values.forEach(value => classes.delete(value)),
            contains: value => classes.has(value),
            toggle: (value, force = !classes.has(value)) => force ? classes.add(value) : classes.delete(value)
        };
    }
    addEventListener(name, fn) {
        if (!this.events.has(name)) this.events.set(name, []);
        this.events.get(name).push(fn);
    }
    dispatchEvent(event) { this.events.get(event.type)?.forEach(fn => fn(event)); }
    setAttribute(name, value) { this.attributes[name] = value; }
    appendChild(child) { this.options.push(child); }
    querySelector(selector) { return selector === '[data-sort-indicator]' ? this.indicator ?? null : selector.includes('__RequestVerificationToken') ? { value: 'test-csrf' } : null; }
    closest() { return this.parent ?? null; }
    reportValidity() { return true; }
    querySelectorAll() { return []; }
    scrollIntoView() {}
    focus() {}
}

function harness({ market = 'goals', search = '', savedValues = [], admin = false, lab = false } = {}) {
    const nodes = new Map();
    const document = new Element('document');
    document.getElementById = id => {
        if (!nodes.has(id)) nodes.set(id, new Element(id));
        return nodes.get(id);
    };
    const node = document.getElementById;
    document.createElement = tag => new Element(tag);
    const location = new URL(`http://localhost:5130/BotPicks?market=${market}&${search}`);
    const production = node('BotPicksProductionSurface');
    production.classList.add('d-none');
    const surface = node('BotPicksResearchSurface');
    surface.dataset = { endpoint: '/BotPicks/GeneralPicks', settlementEndpoint: '/BotPicks/SettleGeneralPick', evidenceEndpoint: '/BotPicks/GeneralPickEvidence', labEndpoint: lab ? '/BotPicks/GeneralPicksLab' : '', canSettle: String(admin), marketFamily: market, locale: 'es-CL' };
    const sorts = ['MatchDate', 'BotKey', 'MarketType', 'SelectedOdds', 'ModelDecision', 'PublicationStatus', 'FinalProbability', 'FinalEdge', 'FinalExpectedValue', 'SelectionScore', 'OutcomeStatus', 'EvaluationId'].map(column => {
        const button = new Element(); button.dataset.researchSort = column; button.parent = new Element('th'); button.indicator = new Element('span'); return button;
    });
    const tabs = ['research', 'production'].map(name => {
        const tab = new Element();
        tab.dataset.botPicksSurface = name;
        return tab;
    });
    const markets = ['corners', 'goals', 'shots', 'sog'].map(name => {
        const link = new Element();
        link.href = `http://localhost:5130/BotPicks?market=${name}&DateFrom=2026-09-01&DateTo=2026-09-06`;
        return link;
    });
    document.querySelectorAll = selector => selector === '[data-bot-picks-surface]' ? tabs
        : selector === '.bot-market-switcher-link' ? markets : selector === '[data-research-sort]' ? sorts : [];
    for (const id of ['DateFrom', 'BotResearchDateFrom']) node(id).value = location.searchParams.get('dateFrom') || '2026-09-01';
    for (const id of ['DateTo', 'BotResearchDateTo']) node(id).value = location.searchParams.get('dateTo') || '2026-09-06';
    node('BotPicksMonth').value = '2026-09';
    node('BotPicksInitialBankroll').value = '100';
    node('BotPicksFlatBetStake').value = '1000';
    node('BotPicksBetCurrency').value = 'CLP';
    node('BotPicksServerMonitoring').open = true;
    node('BotPicksMonitoringAudit').open = true;
    const suffix = { corners: 'Corners', goals: 'Goals', shots: 'Shots', sog: 'ShotsOnGoal' }[market];
    const optionValues = {
        BotResearchMarketType: ['', `Total${suffix}`, `HomeTeam${suffix}`, `AwayTeam${suffix}`],
        BotResearchBotKey: ['', 'A', 'C', 'D', 'E', 'F', 'H'],
        BotResearchModelDecision: ['', 'Approved', 'Rejected', 'Abstain', 'PendingData', 'Invalid', 'NotRecorded'],
        BotResearchPublicationStatus: ['', 'Published', 'ProductionBlocked', 'ModelRejected', 'PendingData', 'Eligible', 'Shadow', 'NotSelected', 'NotPublished'],
        BotResearchPageSize: ['25', '50', '100']
    };
    for (const [id, values] of Object.entries(optionValues)) node(id).options = values.map(value => ({ value }));
    node('BotResearchPageSize').value = '50';
    node('BotResearchModelDecision').value = 'Approved';
    const saved = new Map(savedValues);
    const calls = [];
    const context = vm.createContext({
        document, bootstrap: { Modal: { getOrCreateInstance: () => ({ show() {}, hide() {} }) } },
        localStorage: { getItem: key => saved.get(key) ?? null, setItem: (key, value) => saved.set(key, value) },
        window: { bootstrap: true, location, history: { replaceState(_state, _unused, value) { location.href = new URL(value, location).href; } }, setTimeout },
        fetch: (url, options = {}) => new Promise(resolve => calls.push({ url, options, resolve })),
        CustomEvent: class { constructor(type, values = {}) { this.type = type; this.detail = values.detail; } },
        crypto: { randomUUID }, AbortController, URL, URLSearchParams, Intl, Date, setTimeout, clearTimeout, console
    });
    vm.runInContext(inlineTemplate.replaceAll("'@marketFamily'", `'${market}'`), context);
    assert.equal(calls.length, 0, 'the production script waits for the surface controller');
    vm.runInContext(script, context);
    return { calls, node, document, tabs, markets, location, saved, sorts };
}

const flush = () => new Promise(resolve => setImmediate(resolve));
async function respond(call, items = [], availableBots = []) {
    assert.ok(call, 'expected request');
    call.resolve({ ok: true, json: async () => ({ items, availableBots, totalCount: items.length, page: 1, pageSize: 50, totalPages: items.length ? 1 : 0 }) });
    await flush();
}
function submit(page) {
    page.node('BotResearchFilters').dispatchEvent({ type: 'submit', preventDefault() {} });
}
const getQuery = call => new URL(call.url, 'http://localhost').searchParams;

assert.ok(view.indexOf('id="BotPicksResearchTab"') < view.indexOf('id="BotPicksProductionTab"'), 'general tab is first');
assert.match(partial, /Bot Picks generales/);
assert.match(partial, /<option value="Approved" selected>Aprobada por modelo<\/option>/);
assert.match(partial, /<option value="ProductionBlocked">Bloqueados<\/option>/);

const page = harness({ savedValues: [['bot-picks-goals-surface', 'production']] });
assert.equal(page.calls.length, 1, 'legacy Production preference must open only the paged general request');
assert.match(page.calls[0].url, /^\/BotPicks\/GeneralPicks\?/);
assert.equal(getQuery(page.calls[0]).get('marketFamily'), 'goals');
assert.equal(getQuery(page.calls[0]).get('modelDecision'), 'Approved', 'general picks start with every model-approved signal');
for (const field of ['marketType', 'publicationStatus', 'botKey']) {
    assert.equal(getQuery(page.calls[0]).has(field), false, `${field} must not hide any general picks by default`);
}
assert.equal(page.node('BotPicksProductionSurface').classList.contains('d-none'), true);
assert.equal(page.node('BotPicksResearchSurface').classList.contains('d-none'), false);
const rows = [
    { evaluationId: 1, marketType: 'HomeTeamGoals', publicationStatus: 'ProductionBlocked', modelDecision: 'Approved', botKey: 'C2026' },
    { evaluationId: 2, marketType: 'AwayTeamGoals', publicationStatus: 'Published', modelDecision: 'Approved', botKey: 'F2026', publishedSelectionId: 123 },
    { evaluationId: 3, marketType: 'TotalGoals', publicationStatus: 'ProductionBlocked', modelDecision: 'Approved', botKey: 'C2026' },
    { evaluationId: 4, marketType: 'TotalGoals', publicationStatus: 'ModelRejected', modelDecision: 'Rejected', botKey: 'D2026' },
    { evaluationId: 5, marketType: 'HomeTeamGoals', publicationStatus: 'Shadow', modelDecision: 'Approved', botKey: 'H' }
];
await respond(page.calls[0], rows);
assert.equal(page.calls.length, 1, 'showing general picks must not request scorecards or production histories');
for (const label of ['Goles local', 'Goles visita', 'Goles totales', 'Bloqueada por gate', 'Publicada', 'Shadow', 'Rechazada']) {
    assert.ok(page.node('BotResearchTableBody').innerHTML.includes(label), `${label} must remain visible`);
}
assert.doesNotMatch(page.node('BotResearchTableBody').innerHTML, /Betting\/Create/);

const legacy = harness();
await respond(legacy.calls[0], [{
    evaluationId: -124, publishedSelectionId: 124, recordKind: 'PublishedSelection',
    botKey: 'my-clone', modelDecision: 'NotRecorded', publicationStatus: 'Published', marketType: 'HomeTeamGoals'
}], [{ botKey: 'my-clone', displayName: 'Mi bot personalizado' }, { botKey: 'A', displayName: 'Bot A Actual' }]);
assert.equal(legacy.calls.length, 1, 'the bot catalog must arrive with the page, without another request');
assert.ok(legacy.node('BotResearchBotKey').options.some(option => option.value === 'my-clone' && option.textContent === 'Mi bot personalizado'));
assert.match(legacy.node('BotResearchTableBody').innerHTML, /Mi bot personalizado/);
assert.match(legacy.node('BotResearchTableBody').innerHTML, /Sin registro de decisión/);
assert.match(legacy.node('BotResearchTableBody').innerHTML, /Pick publicado sin evaluación registrada/);
assert.doesNotMatch(legacy.node('BotResearchTableBody').innerHTML, /Evaluación #-124|Betting\/Create/);
legacy.node('BotResearchTableBody').dispatchEvent({ type: 'click', target: { closest: () => ({ dataset: { researchDetail: 'published-124' } }) } });
assert.match(legacy.node('BotResearchDetailBody').innerHTML, /Sin evaluación registrada/);
assert.doesNotMatch(legacy.node('BotResearchDetailBody').innerHTML, /Candidato evaluado|Ganador del grupo|<strong>-124<\/strong>/);
legacy.node('BotResearchBotKey').value = 'my-clone';
submit(legacy);
assert.equal(getQuery(legacy.calls[1]).get('botKey'), 'my-clone');
const customLink = harness({ search: 'botKey=my-clone&modelDecision=NotRecorded' });
assert.equal(getQuery(customLink.calls[0]).get('botKey'), 'my-clone', 'a linked custom bot is filtered before the catalog has loaded');
assert.equal(getQuery(customLink.calls[0]).get('modelDecision'), 'NotRecorded');

page.node('BotResearchPublicationStatus').value = 'ProductionBlocked';
submit(page);
assert.equal(getQuery(page.calls[1]).get('publicationStatus'), 'ProductionBlocked', 'blocked filter must be explicitly applied to the paged backend query');
await respond(page.calls[1], [rows[0], rows[2]]);
page.node('BotResearchPublicationStatus').value = '';
submit(page);
assert.equal(getQuery(page.calls[2]).has('publicationStatus'), false, 'Todos removes the production-state restriction');
await respond(page.calls[2], rows);

page.node('BotResearchDateFrom').value = '2026-09-02';
page.node('BotResearchDateTo').value = '2026-09-05';
page.node('BotResearchFilters').dispatchEvent({ type: 'change' });
for (const link of page.markets) {
    const url = new URL(link.href);
    assert.equal(url.searchParams.get('surface'), 'general');
    assert.equal(url.searchParams.get('dateFrom'), '2026-09-02');
    assert.equal(url.searchParams.get('dateTo'), '2026-09-05');
    assert.equal(url.searchParams.has('marketType'), false, 'switching market keeps all local/away/total scopes');
    assert.equal(url.searchParams.has('publicationStatus'), false);
}
page.node('BotResearchMarketType').value = 'HomeTeamGoals';
page.node('BotResearchPublicationStatus').value = 'ProductionBlocked';
page.node('BotResearchFilters').dispatchEvent({ type: 'change' });
const cornersLink = new URL(page.markets[0].href);
assert.equal(cornersLink.searchParams.get('marketType'), 'HomeTeamCorners', 'an explicit local scope maps to the destination family');
assert.equal(cornersLink.searchParams.get('publicationStatus'), 'ProductionBlocked');
const corners = harness({ market: 'corners', search: cornersLink.searchParams.toString() });
assert.equal(getQuery(corners.calls[0]).get('marketFamily'), 'corners');
assert.equal(getQuery(corners.calls[0]).get('marketType'), 'HomeTeamCorners');
assert.equal(getQuery(corners.calls[0]).get('publicationStatus'), 'ProductionBlocked');

const explicitProduction = harness({ search: 'surface=production' });
assert.equal(explicitProduction.calls.length, 1);
assert.match(explicitProduction.calls[0].url, /^\/BotPicks\/Selections\?/);
explicitProduction.node('DateFrom').value = '2026-09-03';
explicitProduction.node('MarketType').value = 'AwayTeamGoals';
explicitProduction.markets[2].dispatchEvent({ type: 'click' });
const productionMarketLink = new URL(explicitProduction.markets[2].href);
assert.equal(productionMarketLink.searchParams.get('surface'), 'production');
assert.equal(productionMarketLink.searchParams.get('dateFrom'), '2026-09-03');
assert.equal(productionMarketLink.searchParams.get('marketType'), 'AwayTeamShots');
assert.equal(productionMarketLink.searchParams.has('publicationStatus'), false);
const returningProduction = harness({ savedValues: [['bot-picks-goals-surface-v2', 'production']] });
assert.match(returningProduction.calls[0].url, /^\/BotPicks\/Selections\?/);
const explicitGeneral = harness({ search: 'surface=general', savedValues: [['bot-picks-goals-surface-v2', 'production']] });
assert.match(explicitGeneral.calls[0].url, /^\/BotPicks\/GeneralPicks\?/);

const switcher = harness();
const obsolete = switcher.calls[0];
switcher.tabs[1].dispatchEvent({ type: 'click' });
assert.equal(obsolete.options.signal.aborted, true, 'leaving general cancels its pending query');
switcher.tabs[0].dispatchEvent({ type: 'click' });
assert.equal(switcher.calls.length, 3, 'returning retries the canceled general query');
await respond(switcher.calls[2], [rows[2]]);
await respond(obsolete, [rows[0]]);
assert.match(switcher.node('BotResearchTableBody').innerHTML, /Goles totales/);
assert.doesNotMatch(switcher.node('BotResearchTableBody').innerHTML, /Goles local/);
switcher.tabs[1].dispatchEvent({ type: 'click' });
switcher.tabs[0].dispatchEvent({ type: 'click' });
assert.equal(switcher.calls.length, 3, 'returning to unchanged loaded general picks reuses the rows');

console.log('PASS General default, legacy preference migration, all scopes/states, explicit blocked filter, family/date navigation, production isolation, cancellation and reuse.');

const sorted = harness({ search: 'sortBy=SelectedOdds&sortDirection=desc&page=3' });
assert.equal(getQuery(sorted.calls[0]).get('sortBy'), 'SelectedOdds');
assert.equal(getQuery(sorted.calls[0]).get('sortDirection'), 'desc');
assert.equal(getQuery(sorted.calls[0]).get('page'), '3');
const oddsHeader = sorted.sorts.find(button => button.dataset.researchSort === 'SelectedOdds');
oddsHeader.dispatchEvent({ type: 'click' });
assert.equal(getQuery(sorted.calls[1]).get('page'), '1', 'column sorting restarts global pagination');
assert.equal(getQuery(sorted.calls[1]).get('sortDirection'), 'asc');
assert.equal(oddsHeader.parent.attributes['aria-sort'], 'ascending');
oddsHeader.dispatchEvent({ type: 'click' });
assert.equal(getQuery(sorted.calls[2]).get('sortDirection'), 'desc');
assert.equal(sorted.location.searchParams.get('sortDirection'), 'desc');
assert.equal(sorted.calls[1].options.signal.aborted, true, 'rapid sorting cancels stale requests');

const manual = harness({ admin: true });
await respond(manual.calls[0], [{ evaluationId: 987, botKey: 'C2026', marketType: 'AwayTeamGoals',
    modelDecision: 'Approved', publicationStatus: 'ProductionBlocked', selectedSide: 'Over', lineValue: .25,
    selectedOdds: 1.9, outcomeStatus: 'Unavailable', homeTeam: 'Local', awayTeam: 'Visita' }]);
assert.match(manual.node('BotResearchTableBody').innerHTML, /Liquidar manualmente/);
manual.node('BotResearchTableBody').dispatchEvent({ type: 'click', target: {
    closest: selector => selector === '[data-research-settle]' ? { dataset: { researchSettle: '987' } } : null
} });
manual.node('BotResearchActualValue').value = '0';
manual.node('BotResearchActualValue').dispatchEvent({ type: 'input' });
assert.match(manual.node('BotResearchSettlementPreview').textContent, /Media pérdida/);
manual.node('BotResearchSettlementReason').value = 'Confirmado por liga';
manual.node('BotResearchSettlementForm').dispatchEvent({ type: 'submit', preventDefault() {} });
const save = manual.calls.at(-1);
assert.equal(save.options.method, 'PUT');
assert.equal(save.options.headers.RequestVerificationToken, 'test-csrf');
assert.equal(JSON.parse(save.options.body).actualValue, 0, 'zero must not become a missing result');
assert.equal(new URL(save.url, 'http://localhost').searchParams.get('id'), '987');
const idempotencyKey = JSON.parse(save.options.body).requestId;
save.resolve({ ok: false, status: 502, json: async () => ({ error: 'Intenta otra vez' }) });
await flush();
assert.equal(manual.node('BotResearchSettlementError').textContent, 'Intenta otra vez');
manual.node('BotResearchSettlementForm').dispatchEvent({ type: 'submit', preventDefault() {} });
const retry = manual.calls.at(-1);
assert.equal(JSON.parse(retry.options.body).requestId, idempotencyKey, 'retry cannot settle twice');
retry.resolve({ ok: true });
await flush();
assert.match(manual.calls.at(-1).url, /GeneralPicks/);
await respond(manual.calls.at(-1), [{ evaluationId: 987, selectedSide: 'Over', selectedOdds: 1.9,
    outcomeSource: 'Manual', actualValue: 0, outcomeStatus: 'HalfLoss', manualSettlementReason: '<script>alert(1)</script>' }]);
assert.match(manual.node('BotResearchTableBody').innerHTML, /Corregir liquidación/);
assert.match(manual.node('BotResearchTableBody').innerHTML, /&lt;script&gt;/);
assert.doesNotMatch(manual.node('BotResearchTableBody').innerHTML, /<script>/);
assert.doesNotMatch(page.node('BotResearchTableBody').innerHTML, /Liquidar manualmente/);
console.log('PASS global column sorting, URL state, accessible order, manual settlement preview, CSRF, idempotent retry, refresh and escaped audit note.');

const evidence = harness();
await respond(evidence.calls[0], [{ evaluationId: 321, botKey: 'C2026', featureSnapshotJson: '{}' }]);
assert.equal(evidence.calls.length, 1, 'table loading does not fetch feature snapshots');
const openEvidence = () => evidence.node('BotResearchTableBody').dispatchEvent({ type: 'click', target: {
    closest: selector => selector === '[data-research-detail]' ? { dataset: { researchDetail: '321' } } : null
} });
openEvidence();
assert.match(evidence.calls[1].url, /GeneralPickEvidence\?id=321/);
assert.match(evidence.node('BotResearchDetailBody').innerHTML, /Cargando evidencia/);
evidence.calls[1].resolve({ ok: true, json: async () => ({
    featureSnapshotJson: '{"source":"<script>"}',
    modelDecisionReasonsJson: '["APPROVED_FROM_DETAIL"]',
    modelExplanation: 'Explicación bajo demanda'
}) });
await flush();
assert.match(evidence.node('BotResearchDetailBody').innerHTML, /&lt;script&gt;/);
assert.match(evidence.node('BotResearchDetailBody').innerHTML, /APPROVED_FROM_DETAIL/);
assert.match(evidence.node('BotResearchDetailBody').innerHTML, /Explicación bajo demanda/);
assert.doesNotMatch(evidence.node('BotResearchDetailBody').innerHTML, /Cargando evidencia/);
openEvidence();
assert.equal(evidence.calls.length, 2, 'reopening the same evidence reuses the frozen snapshot');
console.log('PASS evidence loads only on demand, escapes content and reuses the loaded snapshot.');

const lab = harness({ lab: true, search: 'modelDecision=Rejected&publicationStatus=ProductionBlocked' });
await respond(lab.calls[0], rows);
assert.equal(lab.calls.length, 2, 'the lab starts after the table has rendered');
assert.match(lab.calls[1].url, /^\/BotPicks\/GeneralPicksLab\?/);
const labQuery = getQuery(lab.calls[1]);
assert.equal(labQuery.get('marketFamily'), 'goals');
assert.equal(labQuery.get('publicationStatus'), 'ProductionBlocked');
for (const excluded of ['modelDecision', 'page', 'pageSize', 'sortBy', 'sortDirection'])
    assert.equal(labQuery.has(excluded), false, `${excluded} cannot alter the approved aggregate lab`);
lab.calls[1].resolve({ ok: true, json: async () => ({
    summary: {
        approvedEvaluations: 120, independentSignals: 30, uniqueFixtures: 18,
        resolvedSignals: 20, pendingSignals: 6, unavailableSignals: 4,
        profitLossUnits: 2.4, yield: .12, observedWinRate: .55,
        averageModelProbability: .58, calibrationGap: .03, brierScore: .21
    },
    timeline: [
        { date: '2026-09-01', resolvedSignals: 8, dailyProfitLossUnits: -.5, cumulativeProfitLossUnits: -.5 },
        { date: '2026-09-03', resolvedSignals: 12, dailyProfitLossUnits: 2.9, cumulativeProfitLossUnits: 2.4 }
    ],
    calibration: [
        { probabilityFrom: .4, probabilityTo: .6, signals: 9, averageModelProbability: .54, observedWinRate: .56 },
        { probabilityFrom: .6, probabilityTo: .8, signals: 11, averageModelProbability: .64, observedWinRate: .55 }
    ],
    segments: [{ botKey: 'C2026', marketType: 'HomeTeamGoals', resolvedSignals: 20,
        profitLossUnits: 2.4, yield: .12, observedWinRate: .55 }]
}) });
await flush();
assert.match(lab.node('BotResearchLabResolved').textContent, /20.*30/);
assert.match(lab.node('BotResearchLabProfit').textContent, /\+2,4u/);
assert.match(lab.node('BotResearchLabBrier').textContent, /Brier 0,21/);
assert.match(lab.node('BotResearchLabTimeline').innerHTML, /<svg[\s\S]*\+2,4u/);
assert.match(lab.node('BotResearchLabCalibration').innerHTML, /n=9[\s\S]*n=11/);
assert.match(lab.node('BotResearchLabSegments').innerHTML, /Bot C[\s\S]*Goles local/);
assert.match(lab.node('BotResearchLabFootnote').textContent, /120 evaluaciones aprobadas.*30 señales independientes/);
const labSort = lab.sorts.find(button => button.dataset.researchSort === 'SelectedOdds');
labSort.dispatchEvent({ type: 'click' });
await respond(lab.calls[2], rows);
assert.equal(lab.calls.length, 3, 'sorting the table reuses the unchanged aggregate lab');
console.log('PASS approved lab loads after the table, ignores decision/paging/sort, renders performance, calibration and segments, and reuses its slice.');
