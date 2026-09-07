import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const view = fs.readFileSync(path.join(root, 'CornersPrediction.Web/Views/BotPicks/Index.cshtml'), 'utf8');
const inline = view.match(/@section Scripts\s*{\s*<script>([\s\S]*?)<\/script>/)[1]
    .replace(/'@Url\.Action\("([^"]+)"[^\n]*?\)'/g, (_, action) => `'/BotPicks/${action}'`)
    .replace('@(canUpdateResults ? "true" : "false")', 'false')
    .replaceAll("'@language'", "'es'")
    .replaceAll("'@marketFamily'", "'corners'")
    .replaceAll("'@Model.Market.UnitLabel'", "'córners'")
    .replace(/\}\)\(\);\s*$/, 'globalThis.testAccess = { getDecisionReason }; })();');

class Element {
    constructor(id = '') {
        this.id = id;
        this.value = '';
        this.innerHTML = '';
        this.textContent = '';
        this.open = false;
        this.dataset = {};
        this.style = {};
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
    setAttribute() {}
    querySelector() { return null; }
    querySelectorAll() { return []; }
    scrollIntoView() {}
}

function harness(surface = 'production') {
    const nodes = new Map();
    const document = new Element('document');
    document.getElementById = id => {
        if (!nodes.has(id)) nodes.set(id, new Element(id));
        return nodes.get(id);
    };
    const node = document.getElementById;
    node('DateFrom').value = '2026-09-01';
    node('DateTo').value = '2026-09-06';
    node('BotPicksMonth').value = '2026-09';
    node('BotPicksInitialBankroll').value = '100';
    node('BotPicksFlatBetStake').value = '1000';
    node('BotPicksBetCurrency').value = 'CLP';
    node('BotPicksServerMonitoring').open = true;
    node('BotPicksMonitoringAudit').open = true;
    const saved = new Map([['bot-picks-corners-surface', surface]]);
    const calls = [];
    const context = vm.createContext({
        document, bootstrap: { Modal: { getOrCreateInstance: () => ({}) } },
        localStorage: { getItem: key => saved.get(key) ?? null, setItem: (key, value) => saved.set(key, value) },
        window: { location: { pathname: '/BotPicks' }, history: { replaceState() {} }, setTimeout },
        fetch: (url, options = {}) => new Promise(resolve => calls.push({ url, options, resolve })),
        AbortController, URLSearchParams, Intl, Date, setTimeout, clearTimeout, console
    });
    vm.runInContext(inline, context, { filename: 'BotPicks/Index.cshtml inline script' });
    // The separate surface controller now owns initial activation. These tests
    // exercise the production path only after an explicit tab selection.
    if (surface === 'production') {
        document.dispatchEvent({ type: 'bot-picks-surface-change', detail: { surface: 'production' } });
    }
    return { context, calls, node, document };
}
const flush = () => new Promise(resolve => setImmediate(resolve));
async function respond(call, payload = []) {
    assert.ok(call, 'expected request');
    call.resolve({ ok: true, json: async () => payload });
    await flush();
}

const page = harness();
assert.deepEqual(page.calls.map(call => call.url.split('?')[0]), ['/BotPicks/Selections'],
    'the initial request must prioritize selections and avoid hidden annual history');
await respond(page.calls[0]);
assert.deepEqual(page.calls.map(call => call.url.split('?')[0]), [
    '/BotPicks/Selections', '/BotPicks/PerformanceScorecards', '/BotPicks/MonitoringSummary'
]);
assert.equal(page.node('BotPicksDsSettled').textContent, '', 'closed advanced statistics must not be computed');
await respond(page.calls[1]);
await respond(page.calls[2]);
page.node('BotPicksDataScience').open = true;
page.node('BotPicksDataScience').dispatchEvent({ type: 'toggle' });
await new Promise(resolve => setTimeout(resolve, 10));
assert.equal(page.node('BotPicksDsSettled').textContent, '0', 'statistics render when opened');

page.node('BotPicksMonthlyHistory').open = true;
page.node('BotPicksMonthlyHistory').dispatchEvent({ type: 'toggle' });
assert.equal(page.calls[3].url, '/BotPicks/MonthlyHistory');
await respond(page.calls[3]);
page.node('BotPicksMonthlyHistory').open = false;
page.node('BotPicksMonthlyHistory').dispatchEvent({ type: 'toggle' });
page.node('BotPicksMonthlyHistory').open = true;
page.node('BotPicksMonthlyHistory').dispatchEvent({ type: 'toggle' });
assert.equal(page.calls.length, 4, 'reopening an unchanged monthly panel reuses its result');

page.node('BotPicksFilters').dispatchEvent({ type: 'submit', preventDefault() {} });
const older = page.calls[4];
page.node('BotPicksFilters').dispatchEvent({ type: 'submit', preventDefault() {} });
const newer = page.calls[5];
assert.ok(older.options.signal.aborted, 'changing filters cancels the superseded selection request');
await respond(newer);
const newestCount = page.node('BotPicksVisibleCount').textContent;
await respond(older, [{ automatedCornerBetSelectionId: 1, matchDate: '2026-09-01', status: 'Pending' }]);
assert.equal(page.node('BotPicksVisibleCount').textContent, newestCount,
    'an older response must never replace the current filters after cancellation');

const pendingRow = {
    automatedCornerBetSelectionId: 101, automationVersion: 'Bot-C2026', botKey: 'C2026',
    matchDate: new Date(Date.now() + 86_400_000).toISOString(), marketType: 'AwayTeamCorners',
    status: 'Pending', homeTeam: 'Home', awayTeam: 'Away', source: 'Pinnacle',
    selectedSide: 'Over', lineValue: 4.5, odds: 1.9, stake: 1,
    productionPlan: { key: 'verifying', label: 'Verificando plan', reason: 'Verificando evidencia', stakeUnits: 0, isProductive: false }
};
const verifiedRow = {
    ...pendingRow,
    productionPlan: { key: 'stake-half', label: 'Apostar 0.5u', stakeUnits: 0.5, isProductive: true }
};
const fast = harness();
fast.node('StrategyPlan').value = 'bet';
assert.match(fast.calls[0].url, /includePerformance=false/);
await respond(fast.calls[0], [pendingRow]);
assert.equal(fast.calls.length, 2, 'the full verification follows the initial rows');
assert.doesNotMatch(fast.calls[1].url, /includePerformance=false/);
assert.equal(fast.node('BotPicksTableWrap').classList.contains('d-none'), false,
    'the table must already be visible while performance remains in flight');
assert.match(fast.node('BotPicksVisibleCount').textContent, /de 1 recomendaciones/);
assert.equal(fast.node('StrategyPlan').disabled, true,
    'a saved productive filter must not hide unclassified rows');
assert.match(fast.node('BotPicksTableBody').innerHTML, /Verificando plan/);
assert.doesNotMatch(fast.node('BotPicksTableBody').innerHTML, /Betting\/Create/,
    'no betting action is available before full verification');
await respond(fast.calls[1], [verifiedRow]);
assert.equal(fast.node('StrategyPlan').disabled, false);
assert.match(fast.node('BotPicksTableBody').innerHTML, /Betting\/Create/,
    'the authorized plan is applied only after the full response');
assert.equal(fast.node('BotPicksPlanVerification').classList.contains('d-none'), true);

const failed = harness();
await respond(failed.calls[0], [pendingRow]);
failed.calls[1].resolve({ ok: false });
await flush();
assert.equal(failed.node('BotPicksTableWrap').classList.contains('d-none'), false,
    'a failed performance query must preserve the usable table');
assert.match(failed.node('BotPicksTableBody').innerHTML, /Plan sin verificar/);
assert.doesNotMatch(failed.node('BotPicksTableBody').innerHTML, /Betting\/Create/);
assert.match(failed.node('BotPicksPlanVerification').textContent, /no se pudo verificar/);
assert.equal(failed.node('StrategyPlan').disabled, true);

const switched = harness();
await respond(switched.calls[0], [pendingRow]);
const obsoleteVerification = switched.calls[1];
switched.node('BotPicksFilters').dispatchEvent({ type: 'submit', preventDefault() {} });
assert.ok(obsoleteVerification.options.signal.aborted);
await respond(obsoleteVerification, [verifiedRow]);
assert.doesNotMatch(switched.node('BotPicksTableBody').innerHTML, /Betting\/Create/,
    'an obsolete second-phase response must never authorize the new filter view');
await respond(switched.calls[2], [{ ...pendingRow, automatedCornerBetSelectionId: 102 }]);
assert.match(switched.node('BotPicksTableBody').innerHTML, /Verificando plan/);
assert.doesNotMatch(switched.node('BotPicksTableBody').innerHTML, /Betting\/Create/);

const research = harness('research');
assert.equal(research.calls.length, 0, 'restoring research must not query the hidden production table');
research.document.dispatchEvent({ type: 'bot-picks-surface-change', detail: { surface: 'production' } });
assert.equal(research.calls.length, 1, 'returning to production loads its table');

const item = { decisionReason: '{"botProfile":"C2026","featureSnapshot":{"score":1}}' };
const read = page.context.testAccess.getDecisionReason;
const first = read(item);
for (let index = 0; index < 100; index++) assert.equal(read(item), first, 'decision parsing must reuse the cached object');
item.decisionReason = '{"botProfile":"F2026"}';
assert.equal(read(item).botProfile, 'F2026', 'changing decision evidence invalidates the parsed cache');
assert.notEqual(read(item), first);
console.log('PASS Bot Picks two-phase table loading, zero-stake verification, failure visibility, canceled-plan isolation, deferred statistics/history, cache and surface switching.');
