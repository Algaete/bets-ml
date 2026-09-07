import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const context = { window: {} };
vm.runInNewContext(fs.readFileSync(new URL('../../CornersPrediction.Web/wwwroot/js/robot-panel-job.js', import.meta.url), 'utf8'), context);
const model = context.window.robotPanelJobState;
let state = model({ recommendationJobId: 'job', status: 'Queued', errorMatches: 0 });
assert.equal(state.terminal, false);
assert.equal(state.percent, null);
assert.equal(state.result.IsSuccess, false);
assert.equal(state.result.Status, 'Queued');
state = model({ Status: 'Running', TotalBatches: 20, ProcessedBatches: 1, CurrentBatchCompletedMatches: 5, CurrentBatchTotalMatches: 10 });
assert.equal(state.percent, 7.5);
assert.match(state.progress, /5\/10/);
state = model({ Status: 'Completed', ErrorMatches: 10, TotalBatches: 19, ProcessedBatches: 19 });
assert.equal(state.terminal, true);
assert.equal(state.percent, 100);
assert.equal(state.result.Status, 'PartialSuccess');
assert.equal(state.result.IsSuccess, false);
assert.match(state.message, /10 errores/);
state = model({ Status: 'Completed', ErrorMatches: 0 });
assert.equal(state.result.IsSuccess, true);
state = model({ Status: 'Failed', LastError: 'SQL error' });
assert.equal(state.result.Status, 'Failed');
assert.equal(state.message, 'SQL error');
state = model({ Status: 'Cancelled' });
assert.equal(state.terminal, true);
assert.equal(state.result.IsSuccess, false);
console.log('PASS queued, active progress, completed with errors, success, failed and cancelled panel jobs');

const razor = fs.readFileSync(new URL('../../CornersPrediction.Web/Views/RobotPanel/Index.cshtml', import.meta.url), 'utf8');
const script = razor.match(/<script>\s*([\s\S]*?)<\/script>/)[1]
    .replace('@Html.Raw(dayOptionsJson)', '[7]')
    .replace('@Html.Raw(labelsJson)', '{}')
    .replaceAll(/@Url.Action\("([^"]+)", "([^"]+)"\)/g, '/$2/$1');
const node = () => ({
    textContent: '', value: '7', style: {}, dataset: {}, events: {},
    classList: { add() {}, remove() {}, toggle() {} },
    addEventListener(event, fn) { this.events[event] = fn; }, setAttribute() {}, removeAttribute() {}
});
const elements = new Map();
const get = id => { if (!elements.has(id)) elements.set(id, node()); return elements.get(id); };
const buttons = ['run-bots', 'full-run', 'match-history'].map(action => Object.assign(node(), { dataset: { action } }));
const storage = new Map([['robot-panel-recommendation-job-v1', 'saved-job']]);
const timers = new Map();
const requests = [];
let jobStatus = 'Running';
let jobHttpStatus = 200;
const browser = {
    window: context.window, console,
    document: { getElementById: get, querySelectorAll: () => buttons },
    localStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
    setTimeout: fn => { const id = timers.size + 1; timers.set(id, fn); return id; }, clearTimeout: id => timers.delete(id),
    fetch: async (url, options) => {
        requests.push({ url, options });
        let body = {};
        let status = 200;
        if (url.includes('BotAvailability')) body = { totalOddsRows: 1407, totalMatches: 154, totalBatches: 16 };
        else if (url.includes('BotJobStatus')) {
            status = jobHttpStatus;
            body = status === 200
                ? { recommendationJobId: 'saved-job', status: jobStatus, totalBatches: 16, processedBatches: 1, currentBatchCompletedMatches: 5, currentBatchTotalMatches: 10, errorMatches: jobStatus === 'Completed' ? 10 : 0 }
                : { error: 'No se pudo actualizar el avance. La ejecución continúa en segundo plano.' };
        }
        return { ok: status < 400, status, json: async () => body };
    }
};
vm.runInNewContext(script, browser);
await new Promise(setImmediate);
assert.equal(requests.filter(request => request.url.includes('BotJobStatus')).length, 1);
assert.equal(buttons[0].disabled, true);
assert.equal(buttons[2].disabled, false);
assert.match(get('RobotPanelJobDetail').textContent, /9.4%/);
assert.match(get('RobotPanelBatchRange').textContent, /Todos los 154/);
assert.equal(storage.get('robot-panel-recommendation-job-v1'), 'saved-job');
jobHttpStatus = 502;
await [...timers.values()].at(-1)();
await new Promise(setImmediate);
assert.equal(storage.get('robot-panel-recommendation-job-v1'), 'saved-job');
assert.match(get('RobotPanelJobStage').textContent, /continúa/);
jobHttpStatus = 200;
jobStatus = 'Completed';
await [...timers.values()].at(-1)();
await new Promise(setImmediate);
assert.equal(buttons[0].disabled, false);
assert.equal(storage.has('robot-panel-recommendation-job-v1'), false);
assert.match(get('RobotPanelJobStage').textContent, /10 errores/);
assert.match(get('RobotPanelStatus').className, /alert-warning/);
console.log('PASS real panel script resumes saved job, keeps progress on transient failure, releases buttons and warns on terminal errors');
