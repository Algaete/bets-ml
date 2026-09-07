import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const view = readFileSync(new URL('../../CornersPrediction.Web/Views/BotPicks/Index.cshtml', import.meta.url), 'utf8');
const start = view.indexOf('const renderPerformanceScorecards = () => {');
const end = view.indexOf('const loadPerformanceScorecards = async () => {', start);
assert.ok(start >= 0 && end > start, 'Use the actual production scorecard renderer.');
const row = {
    WindowDays: 30, Dimension: 'BotMarketSideVersion', BotKey: 'F2026',
    MarketType: 'AwayTeamGoals', SelectedSide: 'Over', Bookmaker: null,
    AutomationVersion: 'current-F2026', PredictiveResolved: 65, PredictiveFixtures: 40,
    TrafficLight: 'Amber', ProductionBlocked: false, Yield: .12, CalibrationGap: .02,
    DeltaBrier: -.01, AverageModelProbability: .6, ObservedWinRate: .58, Recommendation: 'Monitor'
};
const body = { innerHTML: '' };
const context = {
    performanceWindow: 30,
    performanceScorecards: [row, { ...row, Dimension: 'BotMarketSideBookmakerVersion', Bookmaker: 'Pinnacle', BotKey: 'OLD-HOUSE-ROW' }],
    scorecardWindowButtons: [], scorecardBody: body,
    scorecardWrap: { classList: { remove() {} } },
    getValue: (value, key) => value[key], number: value => value == null ? null : Number(value),
    escapeHtml: value => String(value), marketLabel: value => value,
    signedClass: () => '', formatPercent: value => String(value * 100), formatCompact: String
};
vm.runInNewContext(`${view.slice(start, end)}\nrenderPerformanceScorecards();`, context);
assert.match(body.innerHTML, /F2026/);
assert.match(body.innerHTML, /Prueba controlada · 0.5u/);
assert.doesNotMatch(body.innerHTML, /OLD-HOUSE-ROW|Pinnacle/);
assert.match(body.innerHTML, /65 \/ 40/);
console.log('PASS current gate displays consolidated evidence and permits a trial without a bookmaker');
