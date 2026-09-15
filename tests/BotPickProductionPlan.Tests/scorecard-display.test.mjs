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
assert.match(body.innerHTML, /Cumple rendimiento · límite 0.5u/);
assert.doesNotMatch(body.innerHTML, /OLD-HOUSE-ROW|Pinnacle/);
assert.match(body.innerHTML, /65 \/ 40/);
for (const bot of ['C2026', 'F2026', 'E2026']) {
    for (const [yieldValue, qualifies] of [[.029999, false], [.03, true], [.12, true], [null, false]]) {
        context.performanceScorecards = [{ ...row, BotKey: bot, Yield: yieldValue,
            TrafficLight: 'Red', ProductionBlocked: true, PredictiveFixtures: 1,
            CalibrationGap: .2, DeltaBrier: .1 }];
        vm.runInNewContext('renderPerformanceScorecards();', context);
        if (qualifies) assert.match(body.innerHTML, /Cumple rendimiento/);
        else assert.match(body.innerHTML, yieldValue === null ? /Sin rendimiento disponible/ : /Bloqueado: rendimiento < 3%/);
    }
}
context.performanceWindow = 7;
context.performanceScorecards = [{ ...row, WindowDays: 7, Yield: .03 }];
vm.runInNewContext('renderPerformanceScorecards();', context);
assert.match(body.innerHTML, /Ventana informativa/);
assert.doesNotMatch(body.innerHTML, /Cumple rendimiento/);
console.log('PASS scorecard renderer uses inclusive 3% yield regardless of diagnostic color, sample or bookmaker');
