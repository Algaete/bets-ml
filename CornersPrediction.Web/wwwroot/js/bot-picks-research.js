(() => {
    const researchSurface = document.getElementById('BotPicksResearchSurface');
    const productionSurface = document.getElementById('BotPicksProductionSurface');
    const surfaceTabs = Array.from(document.querySelectorAll('[data-bot-picks-surface]'));

    if (!researchSurface || !productionSurface || surfaceTabs.length === 0) {
        return;
    }

    const endpoint = researchSurface.dataset.endpoint ?? '';
    const labEndpoint = researchSurface.dataset.labEndpoint ?? '';
    const settlementEndpoint = researchSurface.dataset.settlementEndpoint ?? '';
    const evidenceEndpoint = researchSurface.dataset.evidenceEndpoint ?? '';
    const evidenceCache = new Map();
    let evidenceController = null;
    const canSettle = researchSurface.dataset.canSettle === 'true';
    const sortButtons = Array.from(document.querySelectorAll('[data-research-sort]'));
    const marketFamily = researchSurface.dataset.marketFamily ?? 'corners';
    const locale = researchSurface.dataset.locale ?? 'es-CL';
    // Version the preference so the former Production default does not hide
    // general picks for returning users after this change.
    const storageKey = `bot-picks-${marketFamily}-surface-v2`;
    const productionOnlyElements = Array.from(document.querySelectorAll('[data-production-only]'));
    const marketLinks = Array.from(document.querySelectorAll('.bot-market-switcher-link'));
    const filtersForm = document.getElementById('BotResearchFilters');
    const dateFromInput = document.getElementById('BotResearchDateFrom');
    const dateToInput = document.getElementById('BotResearchDateTo');
    const marketTypeSelect = document.getElementById('BotResearchMarketType');
    const botKeySelect = document.getElementById('BotResearchBotKey');
    const modelDecisionSelect = document.getElementById('BotResearchModelDecision');
    const publicationStatusSelect = document.getElementById('BotResearchPublicationStatus');
    const pageSizeSelect = document.getElementById('BotResearchPageSize');
    const resetButton = document.getElementById('BotResearchReset');
    const refreshButton = document.getElementById('BotResearchRefresh');
    const loadingState = document.getElementById('BotResearchLoading');
    const errorState = document.getElementById('BotResearchError');
    const emptyState = document.getElementById('BotResearchEmpty');
    const tableWrap = document.getElementById('BotResearchTableWrap');
    const tableBody = document.getElementById('BotResearchTableBody');
    const visibleCount = document.getElementById('BotResearchVisibleCount');
    const totalCountElement = document.getElementById('BotResearchTotalCount');
    const pageScopeElement = document.getElementById('BotResearchPageScope');
    const pagination = document.getElementById('BotResearchPagination');
    const paginationSummary = document.getElementById('BotResearchPaginationSummary');
    const paginationButtons = document.getElementById('BotResearchPaginationButtons');
    const detailModalElement = document.getElementById('BotResearchDetailModal');
    const detailModal = detailModalElement && window.bootstrap
        ? bootstrap.Modal.getOrCreateInstance(detailModalElement)
        : null;
    const detailSubtitle = document.getElementById('BotResearchDetailSubtitle');
    const detailBody = document.getElementById('BotResearchDetailBody');
    const labLoading = document.getElementById('BotResearchLabLoading');
    const labError = document.getElementById('BotResearchLabError');
    const labEmpty = document.getElementById('BotResearchLabEmpty');
    const labContent = document.getElementById('BotResearchLabContent');
    const labResolved = document.getElementById('BotResearchLabResolved');
    const labCoverage = document.getElementById('BotResearchLabCoverage');
    const labProfit = document.getElementById('BotResearchLabProfit');
    const labPending = document.getElementById('BotResearchLabPending');
    const labYield = document.getElementById('BotResearchLabYield');
    const labCalibrationGap = document.getElementById('BotResearchLabCalibrationGap');
    const labBrier = document.getElementById('BotResearchLabBrier');
    const labTimeline = document.getElementById('BotResearchLabTimeline');
    const labCalibration = document.getElementById('BotResearchLabCalibration');
    const labSegments = document.getElementById('BotResearchLabSegments');
    const labFootnote = document.getElementById('BotResearchLabFootnote');
    const hasLabSurface = Boolean(labEndpoint && labLoading && labError && labEmpty && labContent
        && labResolved && labCoverage && labProfit && labPending && labYield && labCalibrationGap
        && labBrier && labTimeline && labCalibration && labSegments && labFootnote);

    if (!endpoint || !filtersForm || !dateFromInput || !dateToInput || !marketTypeSelect
        || !botKeySelect || !modelDecisionSelect || !publicationStatusSelect || !pageSizeSelect
        || !loadingState || !errorState || !emptyState || !tableWrap || !tableBody
        || !visibleCount || !totalCountElement || !pageScopeElement || !pagination
        || !paginationSummary || !paginationButtons) {
        return;
    }

    const initialDateFrom = dateFromInput.value;
    const initialDateTo = dateToInput.value;
    const rowsById = new Map();
    const botNames = new Map();
    const integerFormatter = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 });
    const decimalFormatter = new Intl.NumberFormat(locale, {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });
    const dateFormatter = new Intl.DateTimeFormat(locale, {
        year: 'numeric',
        month: 'short',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
    });
    const dateOnlyFormatter = new Intl.DateTimeFormat(locale, {
        year: 'numeric',
        month: 'short',
        day: '2-digit'
    });

    let sortBy = 'MatchDate';
    let sortDirection = 'desc';
    let activeSurface = 'research';
    let rows = [];
    let currentPage = 1;
    let pageSize = 50;
    let totalCount = 0;
    let totalPages = 0;
    let hasLoaded = false;
    let requestController = null;
    let labController = null;
    let labRequestSignature = null;
    let labLoadedSignature = null;

    const getValue = (item, name) => {
        if (item === null || item === undefined) {
            return null;
        }

        const camelName = name.charAt(0).toLowerCase() + name.slice(1);
        return item[camelName] ?? item[name] ?? null;
    };

    const number = value => {
        if (value === null || value === undefined || value === '') {
            return null;
        }

        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : null;
    };

    const escapeHtml = value => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');

    const formatDate = value => {
        if (!value) return '-';
        const parsed = new Date(value);
        return Number.isNaN(parsed.getTime()) ? '-' : dateFormatter.format(parsed);
    };

    const formatDateOnly = value => {
        if (!value) return '-';
        const parsed = new Date(value);
        return Number.isNaN(parsed.getTime()) ? '-' : dateOnlyFormatter.format(parsed);
    };

    const formatNumber = value => {
        const parsed = number(value);
        return parsed === null ? '-' : decimalFormatter.format(parsed);
    };

    const formatPercent = value => {
        const parsed = number(value);
        if (parsed === null) return '-';
        const percent = Math.abs(parsed) <= 1 ? parsed * 100 : parsed;
        return `${decimalFormatter.format(percent)}%`;
    };

    const formatPoints = value => {
        const parsed = number(value);
        if (parsed === null) return '-';
        const points = Math.abs(parsed) <= 1 ? parsed * 100 : parsed;
        return `${points >= 0 ? '+' : ''}${decimalFormatter.format(points)} pp`;
    };

    const signedClass = value => {
        const parsed = number(value);
        if (parsed === null || parsed === 0) return 'text-muted';
        return parsed > 0 ? 'text-success' : 'text-danger';
    };

    const humanize = value => {
        const raw = String(value ?? '').trim();
        if (!raw) return '-';
        const labels = {
            Approved: 'Aprobada',
            Rejected: 'Rechazada',
            Abstain: 'Abstención',
            PendingData: 'Faltan datos',
            Invalid: 'Inválida',
            NotRecorded: 'Sin registro de decisión',
            ModelRejected: 'Modelo rechazó',
            Eligible: 'Elegible',
            ProductionBlocked: 'Bloqueada por gate',
            Blocked: 'Bloqueada',
            NotEvaluated: 'No evaluada en producción',
            Shadow: 'Shadow',
            NotSelected: 'No seleccionada',
            LowerRanked: 'Perdió ranking',
            RobustBlocked: 'Bloqueada por robustez',
            Published: 'Publicada',
            NotPublished: 'No publicada',
            Won: 'Ganada',
            Lost: 'Perdida',
            Win: 'Ganada',
            HalfWin: 'Media ganancia',
            Loss: 'Perdida',
            HalfLoss: 'Media pérdida',
            Push: 'Devuelta',
            Void: 'Anulada',
            Pending: 'Pendiente',
            Official: 'Dato oficial',
            Unavailable: 'Resultado no disponible'
        };
        return labels[raw] ?? raw;
    };

    const decisionBadgeClass = value => {
        const normalized = String(value ?? '').toLowerCase();
        if (normalized === 'approved') return 'is-approved';
        if (normalized === 'pendingdata' || normalized === 'abstain') return 'is-pending';
        if (normalized === 'rejected' || normalized === 'invalid') return 'is-rejected';
        return 'is-neutral';
    };

    const productionBadgeClass = value => {
        const normalized = String(value ?? '').toLowerCase();
        if (normalized === 'published') return 'is-published';
        if (normalized === 'eligible') return 'is-eligible';
        if (normalized === 'shadow') return 'is-shadow';
        if (['productionblocked', 'blocked', 'robustblocked'].includes(normalized)) return 'is-blocked';
        if (['modelrejected', 'notselected', 'lowerranked'].includes(normalized)) return 'is-not-selected';
        return 'is-neutral';
    };

    const normalizeReasons = row => {
        const value = getValue(row, 'ModelDecisionReasons');
        if (Array.isArray(value)) {
            return value.map(reason => String(reason ?? '').trim()).filter(Boolean);
        }

        if (typeof value !== 'string' || !value.trim()) {
            return [];
        }

        try {
            const parsed = JSON.parse(value);
            return Array.isArray(parsed)
                ? parsed.map(reason => String(reason ?? '').trim()).filter(Boolean)
                : [value.trim()];
        } catch {
            return [value.trim()];
        }
    };

    const isPublishedOnly = row => getValue(row, 'ModelDecision') === 'NotRecorded'
        || getValue(row, 'RecordKind') === 'PublishedSelection';

    const rowIdentity = (row, index = 0) => {
        if (isPublishedOnly(row)) return `published-${getValue(row, 'PublishedSelectionId') ?? index}`;
        const id = getValue(row, 'EvaluationId');
        return id === null ? `row-${currentPage}-${index}` : String(id);
    };

    const normalizeBotKey = key => String(key ?? '').trim().replace(/^([ACDEFH])2026$/i, '$1').toLowerCase();
    const botLabel = key => botNames.get(normalizeBotKey(key))
        || (/^[ACDEFH](2026)?$/i.test(key) ? `Bot ${String(key).charAt(0).toUpperCase()}` : String(key || '-'));
    const addBotOption = (key, label) => {
        const normalized = normalizeBotKey(key);
        if (!normalized) return null;
        let option = Array.from(botKeySelect.options).find(item => normalizeBotKey(item.value) === normalized);
        if (!option) {
            option = document.createElement('option');
            option.value = key;
            botKeySelect.appendChild(option);
        }
        option.textContent = label || botLabel(key);
        botNames.set(normalized, option.textContent);
        return option;
    };

    const marketLabel = value => {
        const labels = {
            TotalCorners: 'Córners totales',
            HomeTeamCorners: 'Córners local',
            AwayTeamCorners: 'Córners visita',
            TotalGoals: 'Goles totales',
            HomeTeamGoals: 'Goles local',
            AwayTeamGoals: 'Goles visita',
            TotalShots: 'Tiros totales',
            HomeTeamShots: 'Tiros local',
            AwayTeamShots: 'Tiros visita',
            TotalShotsOnGoal: 'Tiros al arco totales',
            HomeTeamShotsOnGoal: 'Tiros al arco local',
            AwayTeamShotsOnGoal: 'Tiros al arco visita'
        };
        return labels[value] ?? value ?? '-';
    };

    const formatUnits = value => {
        const parsed = number(value);
        if (parsed === null) return '—';
        return `${parsed > 0 ? '+' : ''}${decimalFormatter.format(parsed)}u`;
    };

    const setSignedValue = (element, value, text) => {
        element.textContent = text;
        element.classList.remove('text-success', 'text-danger', 'text-muted');
        element.classList.add(signedClass(value));
    };

    const renderLabTimeline = values => {
        if (!Array.isArray(values) || values.length === 0) {
            labTimeline.innerHTML = '<div class="text-muted small py-5 text-center">Sin liquidaciones para trazar.</div>';
            return;
        }
        const points = values.map(value => ({
            date: getValue(value, 'Date'),
            cumulative: number(getValue(value, 'CumulativeProfitLossUnits')) ?? 0
        }));
        const width = 760;
        const height = 190;
        const left = 48;
        const right = 18;
        const top = 16;
        const bottom = 34;
        const min = Math.min(0, ...points.map(point => point.cumulative));
        const max = Math.max(0, ...points.map(point => point.cumulative));
        const spread = Math.max(1, max - min);
        const x = index => left + (points.length === 1 ? (width - left - right) / 2
            : index * (width - left - right) / (points.length - 1));
        const y = value => top + (max - value) * (height - top - bottom) / spread;
        const line = points.map((point, index) => `${index === 0 ? 'M' : 'L'}${x(index).toFixed(1)},${y(point.cumulative).toFixed(1)}`).join(' ');
        const area = `${line} L${x(points.length - 1).toFixed(1)},${y(0).toFixed(1)} L${x(0).toFixed(1)},${y(0).toFixed(1)} Z`;
        const yTicks = Array.from(new Set([max, 0, min])).sort((a, b) => b - a);
        const last = points.at(-1);
        labTimeline.innerHTML = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="Resultado acumulado: ${escapeHtml(formatUnits(last.cumulative))} en ${points.length} días con liquidaciones">
            ${yTicks.map(tick => `<line class="${tick === 0 ? 'lab-zero' : 'lab-grid'}" x1="${left}" y1="${y(tick).toFixed(1)}" x2="${width - right}" y2="${y(tick).toFixed(1)}"></line>
                <text x="${left - 7}" y="${(y(tick) + 4).toFixed(1)}" text-anchor="end">${escapeHtml(formatUnits(tick))}</text>`).join('')}
            <path class="lab-area" d="${area}"></path>
            <path class="lab-series" d="${line}"></path>
            <circle class="lab-observed" cx="${x(points.length - 1).toFixed(1)}" cy="${y(last.cumulative).toFixed(1)}" r="5"></circle>
            <text x="${left}" y="${height - 9}">${escapeHtml(formatDateOnly(points[0].date))}</text>
            <text x="${width - right}" y="${height - 9}" text-anchor="end">${escapeHtml(formatDateOnly(last.date))}</text>
            <text class="lab-value" x="${Math.min(width - right, x(points.length - 1) - 7).toFixed(1)}" y="${Math.max(top + 11, y(last.cumulative) - 9).toFixed(1)}" text-anchor="end">${escapeHtml(formatUnits(last.cumulative))}</text>
        </svg>`;
    };

    const renderLabCalibration = values => {
        if (!Array.isArray(values) || values.length === 0) {
            labCalibration.innerHTML = '<div class="text-muted small py-5 text-center">Falta muestra binaria con probabilidad.</div>';
            return;
        }
        const width = 400;
        const height = 190;
        const left = 44;
        const right = 22;
        const top = 18;
        const bottom = 40;
        const x = value => left + Math.max(0, Math.min(1, value)) * (width - left - right);
        const y = value => top + (1 - Math.max(0, Math.min(1, value))) * (height - top - bottom);
        const bins = values.map(value => ({
            from: number(getValue(value, 'ProbabilityFrom')) ?? 0,
            to: number(getValue(value, 'ProbabilityTo')) ?? 0,
            model: number(getValue(value, 'AverageModelProbability')) ?? 0,
            observed: number(getValue(value, 'ObservedWinRate')) ?? 0,
            signals: number(getValue(value, 'Signals')) ?? 0
        }));
        labCalibration.innerHTML = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="Calibración de ${bins.reduce((sum, bin) => sum + bin.signals, 0)} señales binarias">
            <line class="lab-reference" x1="${x(0)}" y1="${y(0)}" x2="${x(1)}" y2="${y(1)}"></line>
            <text x="${left - 7}" y="${y(1) + 4}" text-anchor="end">100%</text>
            <text x="${left - 7}" y="${y(.5) + 4}" text-anchor="end">50%</text>
            <text x="${left - 7}" y="${y(0) + 4}" text-anchor="end">0%</text>
            ${bins.map(bin => `<g>
                <line class="lab-gap" x1="${x(bin.model).toFixed(1)}" y1="${y(bin.model).toFixed(1)}" x2="${x(bin.model).toFixed(1)}" y2="${y(bin.observed).toFixed(1)}"></line>
                <circle class="lab-forecast" cx="${x(bin.model).toFixed(1)}" cy="${y(bin.model).toFixed(1)}" r="4"></circle>
                <circle class="lab-observed" cx="${x(bin.model).toFixed(1)}" cy="${y(bin.observed).toFixed(1)}" r="5"></circle>
                <text x="${x(bin.model).toFixed(1)}" y="${height - 21}" text-anchor="middle">${Math.round(bin.from * 100)}–${Math.round(bin.to * 100)}%</text>
                <text class="lab-value" x="${x(bin.model).toFixed(1)}" y="${Math.max(top + 10, y(bin.observed) - 9).toFixed(1)}" text-anchor="middle">n=${bin.signals}</text>
            </g>`).join('')}
            <text x="${left}" y="${height - 5}">● observado</text>
            <text x="${width - right}" y="${height - 5}" text-anchor="end">○ modelo · diagonal ideal</text>
        </svg>`;
    };

    const renderLab = payload => {
        const summary = getValue(payload, 'Summary') || {};
        const independent = Math.max(0, number(getValue(summary, 'IndependentSignals')) ?? 0);
        const resolved = Math.max(0, number(getValue(summary, 'ResolvedSignals')) ?? 0);
        const pending = Math.max(0, number(getValue(summary, 'PendingSignals')) ?? 0);
        const unavailable = Math.max(0, number(getValue(summary, 'UnavailableSignals')) ?? 0);
        const approved = Math.max(0, number(getValue(summary, 'ApprovedEvaluations')) ?? 0);
        const uniqueFixtures = Math.max(0, number(getValue(summary, 'UniqueFixtures')) ?? 0);
        const profit = number(getValue(summary, 'ProfitLossUnits')) ?? 0;
        const yieldValue = number(getValue(summary, 'Yield'));
        const observed = number(getValue(summary, 'ObservedWinRate'));
        const model = number(getValue(summary, 'AverageModelProbability'));
        const gap = number(getValue(summary, 'CalibrationGap'));
        const brier = number(getValue(summary, 'BrierScore'));

        labEmpty.classList.toggle('d-none', independent > 0);
        labContent.classList.toggle('d-none', independent === 0);
        if (independent === 0) return;

        labResolved.textContent = `${integerFormatter.format(resolved)} / ${integerFormatter.format(independent)}`;
        labCoverage.textContent = `${formatPercent(independent > 0 ? resolved / independent : null)} de cobertura · ${integerFormatter.format(uniqueFixtures)} partidos`;
        setSignedValue(labProfit, profit, formatUnits(profit));
        labPending.textContent = `${integerFormatter.format(pending)} pendientes · ${integerFormatter.format(unavailable)} sin dato`;
        setSignedValue(labYield, yieldValue, formatPercent(yieldValue));
        labCalibrationGap.textContent = observed === null || model === null
            ? '—'
            : `${formatPercent(observed)} / ${formatPercent(model)}`;
        labBrier.textContent = `${gap === null ? 'Gap —' : `gap ${formatPoints(gap)}`} · ${brier === null ? 'Brier —' : `Brier ${decimalFormatter.format(brier)}`}`;

        renderLabTimeline(getValue(payload, 'Timeline'));
        renderLabCalibration(getValue(payload, 'Calibration'));
        const segments = getValue(payload, 'Segments');
        labSegments.innerHTML = Array.isArray(segments) && segments.length > 0
            ? segments.map(segment => {
                const segmentProfit = number(getValue(segment, 'ProfitLossUnits')) ?? 0;
                const segmentYield = number(getValue(segment, 'Yield'));
                return `<tr>
                    <td><strong>${escapeHtml(botLabel(getValue(segment, 'BotKey')))}</strong><span class="text-muted"> · ${escapeHtml(marketLabel(getValue(segment, 'MarketType')))}</span></td>
                    <td class="text-end">${integerFormatter.format(number(getValue(segment, 'ResolvedSignals')) ?? 0)}</td>
                    <td class="text-end ${signedClass(segmentProfit)}">${escapeHtml(formatUnits(segmentProfit))}</td>
                    <td class="text-end ${signedClass(segmentYield)}">${escapeHtml(formatPercent(segmentYield))}</td>
                    <td class="text-end">${escapeHtml(formatPercent(getValue(segment, 'ObservedWinRate')))}</td>
                </tr>`;
            }).join('')
            : '<tr><td colspan="5" class="text-muted text-center">Sin segmentos liquidados.</td></tr>';
        labFootnote.textContent = `${integerFormatter.format(approved)} evaluaciones aprobadas se condensan en ${integerFormatter.format(independent)} señales independientes. Los reintentos se deduplican por bot, partido, mercado, lado y línea. Pendientes y resultados no disponibles quedan fuera del yield, acierto y Brier. El gap es probabilidad promedio menos acierto; un Brier más bajo es mejor.`;
    };

    const renderRows = () => {
        rowsById.clear();
        rows.forEach((row, index) => rowsById.set(rowIdentity(row, index), row));

        tableBody.innerHTML = rows.map((row, index) => {
            const id = rowIdentity(row, index);
            const modelDecision = getValue(row, 'ModelDecision');
            const publicationStatus = getValue(row, 'PublicationStatus');
            const productionDecision = getValue(row, 'ProductionDecision');
            const reasons = normalizeReasons(row);
            const explanation = getValue(row, 'ModelExplanation');
            const productionReason = getValue(row, 'ProductionReason');
            const selectedSide = getValue(row, 'SelectedSide');
            const lineValue = getValue(row, 'LineValue');
            const selectedOdds = getValue(row, 'SelectedOdds');
            const outcomeStatus = getValue(row, 'OutcomeStatus');
            const profitLoss = getValue(row, 'ProfitLoss');
            const publishedSelectionId = getValue(row, 'PublishedSelectionId');
            const source = getValue(row, 'Source');
            const isResearchWinner = getValue(row, 'IsResearchWinner') === true;

            return `<tr class="bot-research-row ${decisionBadgeClass(modelDecision)}">
                <td>
                    <strong class="text-nowrap">${escapeHtml(formatDateOnly(getValue(row, 'MatchDate')))}</strong>
                    <div>${escapeHtml(getValue(row, 'HomeTeam') || 'Local')} <span class="text-muted">vs</span> ${escapeHtml(getValue(row, 'AwayTeam') || 'Visita')}</div>
                    <small class="text-muted">${escapeHtml(getValue(row, 'League') || 'Liga sin informar')}</small>
                </td>
                <td>
                    <span class="bot-research-bot">${escapeHtml(botLabel(getValue(row, 'BotKey')))}</span>
                    <small>${escapeHtml(getValue(row, 'AutomationVersion') || 'Sin versión')}</small>
                    <small>${escapeHtml(source || 'Sin fuente')}</small>
                </td>
                <td>
                    <strong>${escapeHtml(marketLabel(getValue(row, 'MarketType')))}</strong>
                    <div class="bot-research-selection">
                        <span>${escapeHtml(selectedSide || '-')} ${escapeHtml(formatNumber(lineValue))}</span>

                    </div>
                </td>
                <td class="text-nowrap">${escapeHtml(formatNumber(selectedOdds))}</td>
                <td>
                    <span class="bot-research-decision-badge ${decisionBadgeClass(modelDecision)}">${escapeHtml(humanize(modelDecision))}</span>
                    ${isPublishedOnly(row) ? '<small>Pick publicado sin evaluación registrada</small>' : ''}
                    ${isResearchWinner ? '<span class="bot-research-winner-badge">Ganador científico</span>' : ''}
                    ${reasons.length > 0 ? `<small class="bot-research-reason-preview">${reasons.slice(0, 2).map(escapeHtml).join(' · ')}</small>` : ''}
                    ${explanation ? `<small class="bot-research-explanation">${escapeHtml(explanation)}</small>` : ''}
                </td>
                <td>
                    <span class="bot-research-production-badge ${productionBadgeClass(publicationStatus)}">${escapeHtml(humanize(publicationStatus))}</span>
                    ${productionDecision && productionDecision !== publicationStatus
                        ? `<small>Decisión: ${escapeHtml(humanize(productionDecision))}</small>`
                        : ''}
                    ${publishedSelectionId ? `<small class="text-success">Pick #${escapeHtml(publishedSelectionId)}</small>` : ''}
                    ${productionReason ? `<small class="bot-research-reason-preview">${escapeHtml(productionReason)}</small>` : ''}
                </td>
                <td class="text-nowrap">
                    <div><span class="text-muted">Modelo</span> <strong>${escapeHtml(formatPercent(getValue(row, 'FinalProbability')))}</strong></div>
                    <div><span class="text-muted">Mercado</span> ${escapeHtml(formatPercent(getValue(row, 'MarketProbability')))}</div>
                </td>
                <td class="text-nowrap ${signedClass(getValue(row, 'FinalEdge'))}">${escapeHtml(formatPoints(getValue(row, 'FinalEdge')))}</td>
                <td class="text-nowrap">${escapeHtml(formatPercent(getValue(row, 'FinalExpectedValue')))}</td>
                <td>${escapeHtml(formatNumber(getValue(row, 'SelectionScore')))}</td>
                <td>
                    <span class="bot-research-outcome ${String(outcomeStatus ?? '').toLowerCase()}">${escapeHtml(humanize(outcomeStatus || 'Sin resultado'))}</span>
                    <small>Real: ${escapeHtml(formatNumber(getValue(row, 'ActualValue')))}</small>
                    ${getValue(row, 'OutcomeSource') === 'Manual' ? '<span class="badge text-bg-secondary">Manual</span>' : ''}
                    ${getValue(row, 'ManualSettlementReason') ? `<small>${escapeHtml(getValue(row, 'ManualSettlementReason'))}</small>` : ''}
                    <small class="${signedClass(profitLoss)}">P/L ${escapeHtml(formatNumber(profitLoss))}</small>
                </td>
                <td class="text-end">
                    <button class="btn btn-sm btn-outline-primary text-nowrap" type="button" data-research-detail="${escapeHtml(id)}">Ver detalle</button>
                    ${canSettle && ['Over', 'Under'].includes(selectedSide) && number(selectedOdds) > 1
                        ? `<button class="btn btn-sm btn-outline-secondary text-nowrap mt-1" type="button" data-research-settle="${escapeHtml(id)}">${getValue(row, 'OutcomeSource') === 'Manual' ? 'Corregir liquidación' : 'Liquidar manualmente'}</button>` : ''}
                    <small class="d-block text-muted mt-1">${isPublishedOnly(row)
                        ? `Pick #${escapeHtml(publishedSelectionId ?? '-')}`
                        : `Evaluación #${escapeHtml(getValue(row, 'EvaluationId') ?? '-')}`}</small>
                </td>
            </tr>`;
        }).join('');
    };

    const renderPagination = () => {
        if (totalCount <= 0) {
            pagination.classList.add('d-none');
            paginationSummary.textContent = '';
            paginationButtons.innerHTML = '';
            return;
        }

        const first = ((currentPage - 1) * pageSize) + 1;
        const last = Math.min(totalCount, first + rows.length - 1);
        paginationSummary.textContent = `${integerFormatter.format(first)}–${integerFormatter.format(last)} de ${integerFormatter.format(totalCount)} picks y señales`;
        pageScopeElement.textContent = `Página ${integerFormatter.format(currentPage)} de ${integerFormatter.format(Math.max(1, totalPages))}`;

        const buttons = [];
        buttons.push(`<button class="bot-picks-page-button" type="button" data-research-page="${currentPage - 1}" ${currentPage <= 1 ? 'disabled' : ''}>Anterior</button>`);

        const pageNumbers = new Set([1, totalPages]);
        for (let page = Math.max(1, currentPage - 2); page <= Math.min(totalPages, currentPage + 2); page += 1) {
            pageNumbers.add(page);
        }

        let previousPage = 0;
        Array.from(pageNumbers).filter(page => page > 0).sort((left, right) => left - right).forEach(page => {
            if (previousPage > 0 && page - previousPage > 1) {
                buttons.push('<span class="bot-research-page-gap" aria-hidden="true">…</span>');
            }
            buttons.push(`<button class="bot-picks-page-button ${page === currentPage ? 'is-active' : ''}" type="button" data-research-page="${page}" ${page === currentPage ? 'aria-current="page"' : ''}>${page}</button>`);
            previousPage = page;
        });

        buttons.push(`<button class="bot-picks-page-button" type="button" data-research-page="${currentPage + 1}" ${currentPage >= totalPages ? 'disabled' : ''}>Siguiente</button>`);
        paginationButtons.innerHTML = buttons.join('');
        pagination.classList.remove('d-none');
    };

    const renderSort = () => {
        sortButtons.forEach(button => {
            const active = button.dataset.researchSort === sortBy;
            button.closest('th')?.setAttribute('aria-sort', active ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none');
            const indicator = button.querySelector('[data-sort-indicator]');
            if (indicator) indicator.textContent = active ? (sortDirection === 'asc' ? '↑' : '↓') : '↕';
        });
    };

    const render = () => {
        renderSort();
        totalCountElement.textContent = integerFormatter.format(totalCount);
        visibleCount.textContent = totalCount === 0
            ? 'Sin picks ni señales para los filtros aplicados.'
            : `${integerFormatter.format(rows.length)} filas en esta página · ${integerFormatter.format(totalCount)} en el universo filtrado.`;
        emptyState.classList.toggle('d-none', rows.length > 0);
        tableWrap.classList.toggle('d-none', rows.length === 0);
        renderRows();
        renderPagination();
    };

    const readError = async response => {
        try {
            const payload = await response.json();
            return getValue(payload, 'Error') || getValue(payload, 'Message') || `Error HTTP ${response.status}`;
        } catch {
            return `Error HTTP ${response.status}`;
        }
    };

    const buildQuery = () => {
        const query = new URLSearchParams();
        if (dateFromInput.value) query.set('dateFrom', dateFromInput.value);
        if (dateToInput.value) query.set('dateTo', dateToInput.value);
        query.set('marketFamily', marketFamily);
        if (marketTypeSelect.value) query.set('marketType', marketTypeSelect.value);
        if (botKeySelect.value) query.set('botKey', botKeySelect.value);
        if (modelDecisionSelect.value) query.set('modelDecision', modelDecisionSelect.value);
        if (publicationStatusSelect.value) query.set('publicationStatus', publicationStatusSelect.value);
        query.set('page', String(currentPage));
        query.set('pageSize', String(pageSize));
        query.set('sortBy', sortBy);
        query.set('sortDirection', sortDirection);
        return query;
    };

    const buildLabQuery = () => {
        const query = buildQuery();
        for (const key of ['modelDecision', 'page', 'pageSize', 'sortBy', 'sortDirection'])
            query.delete(key);
        return query;
    };

    const loadLab = async (force = false) => {
        if (!hasLabSurface || activeSurface !== 'research') return;
        const query = buildLabQuery();
        const signature = query.toString();
        if (!force && (labLoadedSignature === signature
            || (labController && labRequestSignature === signature))) return;

        labController?.abort();
        const currentController = new AbortController();
        labController = currentController;
        labRequestSignature = signature;
        labLoading.classList.remove('d-none');
        labError.classList.add('d-none');
        labEmpty.classList.add('d-none');
        labContent.classList.add('d-none');
        try {
            const response = await fetch(`${labEndpoint}?${signature}`, {
                signal: currentController.signal,
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error(await readError(response));
            const payload = await response.json();
            if (labController !== currentController) return;
            labLoadedSignature = signature;
            renderLab(payload);
        } catch (error) {
            if (error.name === 'AbortError' || labController !== currentController) return;
            labError.textContent = error instanceof Error
                ? error.message
                : 'No se pudo cargar el lab de aprobadas.';
            labError.classList.remove('d-none');
        } finally {
            if (labController === currentController) {
                labController = null;
                labRequestSignature = null;
                labLoading.classList.add('d-none');
            }
        }
    };

    const generalQueryKeys = new Set([
        'datefrom', 'dateto', 'markettype', 'botkey', 'modeldecision',
        'publicationstatus', 'page', 'pagesize', 'marketfamily', 'sortby', 'sortdirection'
    ]);

    const updateNavigationQuery = (query, targetMarket = marketFamily) => {
        query.set('market', targetMarket);
        query.set('surface', activeSurface === 'research' ? 'general' : 'production');

        // Replace scope/date parameters, including MVC's PascalCase spelling,
        // with the filters from the currently visible surface.
        Array.from(query.keys()).forEach(key => {
            if (generalQueryKeys.has(key.toLowerCase())) query.delete(key);
        });
        if (activeSurface === 'research') {
            for (const [key, value] of buildQuery()) {
                if (key !== 'marketFamily') query.set(key, value);
            }
        } else {
            for (const [key, id] of [
                ['dateFrom', 'DateFrom'], ['dateTo', 'DateTo'], ['marketType', 'MarketType'],
                ['status', 'Status'], ['league', 'League'], ['bookmaker', 'Bookmaker']
            ]) {
                Array.from(query.keys()).forEach(existing => {
                    if (existing.toLowerCase() === key.toLowerCase()) query.delete(existing);
                });
                const value = document.getElementById(id)?.value;
                if (value) query.set(key, value);
            }
        }
        if (targetMarket !== marketFamily) {
            query.delete('page');
            const scope = (query.get('marketType') || '').match(/^(Total|HomeTeam|AwayTeam)/)?.[1];
            const suffix = { corners: 'Corners', goals: 'Goals', shots: 'Shots', sog: 'ShotsOnGoal' }[targetMarket];
            if (scope && suffix) query.set('marketType', `${scope}${suffix}`);
            else query.delete('marketType');
        }
        return query;
    };

    const syncMarketLinks = () => {
        marketLinks.forEach(link => {
            const url = new URL(link.href, window.location.href);
            const targetMarket = url.searchParams.get('market') || marketFamily;
            url.search = updateNavigationQuery(url.searchParams, targetMarket).toString();
            link.href = url.toString();
        });
    };

    const syncNavigation = () => {
        const query = updateNavigationQuery(new URLSearchParams(window.location.search));
        window.history.replaceState(null, '', `${window.location.pathname}?${query.toString()}`);
        syncMarketLinks();
    };

    const load = async (forceLab = false) => {
        requestController?.abort();
        const currentController = new AbortController();
        requestController = currentController;
        hasLoaded = false;
        syncNavigation();
        loadingState.classList.remove('d-none');
        errorState.classList.add('d-none');
        emptyState.classList.add('d-none');
        tableWrap.classList.add('d-none');
        pagination.classList.add('d-none');

        try {
            const response = await fetch(`${endpoint}?${buildQuery().toString()}`, {
                signal: currentController.signal,
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) {
                throw new Error(await readError(response));
            }

            const payload = await response.json();
            if (requestController !== currentController) return;
            const availableBots = getValue(payload, 'AvailableBots');
            if (Array.isArray(availableBots)) {
                availableBots.forEach(bot => addBotOption(getValue(bot, 'BotKey'), getValue(bot, 'DisplayName')));
            }
            rows = getValue(payload, 'Items');
            if (!Array.isArray(rows)) rows = [];
            totalCount = Math.max(0, number(getValue(payload, 'TotalCount')) ?? 0);
            pageSize = Math.max(1, number(getValue(payload, 'PageSize')) ?? Number(pageSizeSelect.value) ?? 50);
            currentPage = Math.max(1, number(getValue(payload, 'Page')) ?? currentPage);
            totalPages = Math.max(0, number(getValue(payload, 'TotalPages')) ?? Math.ceil(totalCount / pageSize));
            pageSizeSelect.value = String(pageSize);
            hasLoaded = true;
            render();
        } catch (error) {
            if (error.name === 'AbortError' || requestController !== currentController) {
                return;
            }

            rows = [];
            totalCount = 0;
            totalPages = 0;
            totalCountElement.textContent = '—';
            pageScopeElement.textContent = 'Consulta no disponible';
            tableBody.innerHTML = '';
            errorState.textContent = error instanceof Error
                ? error.message
                : 'No se pudieron cargar los picks generales.';
            errorState.classList.remove('d-none');
            visibleCount.textContent = 'No fue posible consultar los picks y señales.';
        } finally {
            if (requestController === currentController) {
                requestController = null;
                loadingState.classList.add('d-none');
                if (activeSurface === 'research') void loadLab(forceLab);
            }
        }
    };

    const prettySnapshot = value => {
        if (value === null || value === undefined || value === '') {
            return null;
        }

        try {
            const parsed = typeof value === 'string' ? JSON.parse(value) : value;
            return JSON.stringify(parsed, null, 2);
        } catch {
            return String(value);
        }
    };

    const renderDetail = (row, evidenceMessage = '') => {
        if (!detailBody || !detailSubtitle) return;

        const reasons = normalizeReasons(row);
        const snapshot = prettySnapshot(getValue(row, 'FeatureSnapshotJson'));
        const modelDecision = getValue(row, 'ModelDecision');
        const publicationStatus = getValue(row, 'PublicationStatus');
        const productionDecision = getValue(row, 'ProductionDecision');
        const publishedOnly = isPublishedOnly(row);
        detailSubtitle.textContent = `${getValue(row, 'HomeTeam') || 'Local'} vs ${getValue(row, 'AwayTeam') || 'Visita'} · ${formatDate(getValue(row, 'MatchDate'))}`;
        detailBody.innerHTML = `
            <div class="bot-research-detail-decisions">
                <article>
                    <span>Decisión del modelo</span>
                    <strong class="bot-research-decision-badge ${decisionBadgeClass(modelDecision)}">${escapeHtml(humanize(modelDecision))}</strong>
                    <p>${escapeHtml(getValue(row, 'ModelExplanation') || 'Sin explicación textual.')}</p>
                </article>
                <article>
                    <span>Estado productivo</span>
                    <strong class="bot-research-production-badge ${productionBadgeClass(publicationStatus)}">${escapeHtml(humanize(publicationStatus))}</strong>
                    <p>${escapeHtml(getValue(row, 'ProductionReason') || humanize(productionDecision) || 'Sin motivo productivo.')}</p>
                </article>
            </div>

            <div class="bot-research-detail-grid mt-3">
                <div><span>Evaluation ID</span><strong>${publishedOnly ? 'Sin evaluación registrada' : escapeHtml(getValue(row, 'EvaluationId') ?? '-')}</strong></div>
                <div><span>Run ID</span><strong>${publishedOnly ? '-' : escapeHtml(getValue(row, 'RunId') ?? '-')}</strong></div>
                <div><span>Evaluada</span><strong>${publishedOnly ? '-' : escapeHtml(formatDate(getValue(row, 'EvaluatedAtUtc')))}</strong></div>
                <div><span>Bot / automatización</span><strong>${escapeHtml(getValue(row, 'BotKey') || '-')} · ${escapeHtml(getValue(row, 'AutomationVersion') || '-')}</strong></div>
                <div><span>Configuración</span><strong>${escapeHtml(getValue(row, 'ConfigurationVersion') || '-')}</strong></div>
                <div><span>Feature schema</span><strong>${escapeHtml(getValue(row, 'FeatureSchemaVersion') || '-')}</strong></div>
                <div><span>Mercado</span><strong>${escapeHtml(marketLabel(getValue(row, 'MarketType')))}</strong></div>
                <div><span>Selección</span><strong>${escapeHtml(getValue(row, 'SelectedSide') || '-')} ${escapeHtml(formatNumber(getValue(row, 'LineValue')))} @ ${escapeHtml(formatNumber(getValue(row, 'SelectedOdds')))}</strong></div>
                <div><span>Probabilidad modelo</span><strong>${escapeHtml(formatPercent(getValue(row, 'FinalProbability')))}</strong></div>
                <div><span>Probabilidad mercado</span><strong>${escapeHtml(formatPercent(getValue(row, 'MarketProbability')))}</strong></div>
                <div><span>Edge / EV</span><strong>${escapeHtml(formatPoints(getValue(row, 'FinalEdge')))} · ${escapeHtml(formatPercent(getValue(row, 'FinalExpectedValue')))}</strong></div>
                <div><span>Selection score</span><strong>${escapeHtml(formatNumber(getValue(row, 'SelectionScore')))}</strong></div>
                <div><span>Calidad de datos</span><strong>${escapeHtml(formatPercent(getValue(row, 'DataQualityScore')))}</strong></div>
                <div><span>Acuerdo contexto</span><strong>${escapeHtml(formatPercent(getValue(row, 'ContextAgreementScore')))}</strong></div>
                <div><span>Resultado / real</span><strong>${escapeHtml(humanize(getValue(row, 'OutcomeStatus') || 'Sin resultado'))} · ${escapeHtml(formatNumber(getValue(row, 'ActualValue')))}</strong></div>
                <div><span>P/L / disponible</span><strong class="${signedClass(getValue(row, 'ProfitLoss'))}">${escapeHtml(formatNumber(getValue(row, 'ProfitLoss')))} · ${escapeHtml(formatDate(getValue(row, 'OutcomeAvailableUtc')))}</strong></div>
                <div><span>Pick publicado</span><strong>${getValue(row, 'PublishedSelectionId') ? `#${escapeHtml(getValue(row, 'PublishedSelectionId'))}` : 'No publicado'}</strong></div>
                <div><span>Ranking científico</span><strong>${publishedOnly ? 'Sin registro' : getValue(row, 'IsResearchWinner') === true ? 'Ganador del grupo' : 'Candidato evaluado'}</strong></div>
                <div><span>Fuente</span><strong>${escapeHtml(getValue(row, 'Source') || '-')}</strong></div>
                <div><span>Origen del resultado</span><strong>${escapeHtml(getValue(row, 'OutcomeSource') || 'Sin resultado')}</strong></div>
                <div><span>Liquidación manual</span><strong>${escapeHtml(getValue(row, 'ManualSettledBy') || '-')}</strong><p>${escapeHtml(getValue(row, 'ManualSettlementReason') || '')}</p></div>
            </div>

            <section class="bot-research-detail-reasons mt-3">
                <h3 class="h6">Razones de la decisión del modelo</h3>
                ${reasons.length > 0
                    ? `<ul>${reasons.map(reason => `<li><code>${escapeHtml(reason)}</code></li>`).join('')}</ul>`
                    : '<p class="text-muted mb-0">No se registraron códigos de razón.</p>'}
            </section>

            <details class="bot-research-snapshot mt-3" ${snapshot && !publishedOnly ? 'open' : ''}>
                <summary>Feature snapshot · evidencia reproducible</summary>
                ${evidenceMessage
                    ? `<p class="text-muted mt-2 mb-0">${escapeHtml(evidenceMessage)}</p>`
                    : snapshot && !publishedOnly
                    ? `<pre>${escapeHtml(snapshot)}</pre>`
                    : '<p class="text-muted mt-2 mb-0">No se guardó una evaluación con snapshot para este registro.</p>'}
            </details>`;
    };

    const selectSurface = (surface, persist = true) => {
        activeSurface = surface === 'research' ? 'research' : 'production';
        const showResearch = activeSurface === 'research';
        productionSurface.classList.toggle('d-none', showResearch);
        productionSurface.setAttribute('aria-hidden', showResearch ? 'true' : 'false');
        researchSurface.classList.toggle('d-none', !showResearch);
        researchSurface.setAttribute('aria-hidden', showResearch ? 'false' : 'true');
        productionOnlyElements.forEach(element => element.classList.toggle('d-none', showResearch));
        surfaceTabs.forEach(tab => {
            const selected = tab.dataset.botPicksSurface === activeSurface;
            tab.classList.toggle('is-active', selected);
            tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            tab.tabIndex = selected ? 0 : -1;
        });

        if (persist) {
            localStorage.setItem(storageKey, activeSurface);
        }

        if (!showResearch && requestController) {
            requestController.abort();
            requestController = null;
            loadingState.classList.add('d-none');
        }
        if (!showResearch && labController) {
            labController.abort();
            labController = null;
            labRequestSignature = null;
            labLoading?.classList.add('d-none');
        }
        syncNavigation();

        document.dispatchEvent(new CustomEvent('bot-picks-surface-change', {
            detail: { surface: activeSurface }
        }));

        if (showResearch && !hasLoaded && !requestController) {
            void load();
        } else if (showResearch && hasLoaded) {
            void loadLab();
        }
    };

    document.querySelectorAll('[data-bot-picks-show-research]').forEach(button =>
        button.addEventListener('click', () => {
            marketTypeSelect.value = '';
            botKeySelect.value = '';
            modelDecisionSelect.value = 'Approved';
            publicationStatusSelect.value = '';
            currentPage = 1;
            hasLoaded = false;
            requestController?.abort();
            requestController = null;
            selectSurface('research');
        }));

    surfaceTabs.forEach(tab => {
        tab.addEventListener('click', () => selectSurface(tab.dataset.botPicksSurface));
        tab.addEventListener('keydown', event => {
            if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
            event.preventDefault();
            const next = activeSurface === 'production' ? 'research' : 'production';
            selectSurface(next);
            surfaceTabs.find(candidate => candidate.dataset.botPicksSurface === next)?.focus();
        });
    });

    filtersForm.addEventListener('submit', event => {
        event.preventDefault();
        currentPage = 1;
        pageSize = Number(pageSizeSelect.value) || 50;
        void load();
    });
    filtersForm.addEventListener('change', syncMarketLinks);
    marketLinks.forEach(link => link.addEventListener('click', syncMarketLinks));

    resetButton?.addEventListener('click', () => {
        dateFromInput.value = initialDateFrom;
        dateToInput.value = initialDateTo;
        marketTypeSelect.value = '';
        botKeySelect.value = '';
        modelDecisionSelect.value = 'Approved';
        publicationStatusSelect.value = '';
        pageSizeSelect.value = '50';
        currentPage = 1;
        pageSize = 50;
        sortBy = 'MatchDate';
        sortDirection = 'desc';
        renderSort();
        void load();
    });

    refreshButton?.addEventListener('click', () => void load(true));
    pageSizeSelect.addEventListener('change', () => {
        currentPage = 1;
        pageSize = Number(pageSizeSelect.value) || 50;
        void load();
    });

    paginationButtons.addEventListener('click', event => {
        const button = event.target.closest('[data-research-page]');
        if (!button || button.disabled) return;
        const requestedPage = Number(button.dataset.researchPage);
        if (!Number.isInteger(requestedPage) || requestedPage < 1 || requestedPage > totalPages) return;
        currentPage = requestedPage;
        void load().then(() => document.getElementById('BotResearchResultsTitle')?.scrollIntoView({
            behavior: 'smooth',
            block: 'start'
        }));
    });

    tableBody.addEventListener('click', async event => {
        const button = event.target.closest('[data-research-detail]');
        if (!button) return;
        const row = rowsById.get(button.dataset.researchDetail);
        if (!row) return;
        evidenceController?.abort();
        const id = String(getValue(row, 'EvaluationId'));
        const deferred = evidenceEndpoint && Number(id) > 0 && !isPublishedOnly(row);
        const cached = evidenceCache.get(id);
        renderDetail(cached ? { ...row, ...cached } : row,
            deferred && !cached ? 'Cargando evidencia…' : '');
        detailModal?.show();
        if (!deferred || cached) return;
        const controller = new AbortController();
        evidenceController = controller;
        try {
            const response = await fetch(`${evidenceEndpoint}?id=${encodeURIComponent(id)}`, {
                signal: controller.signal, headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error(await readError(response));
            const payload = await response.json();
            if (evidenceController !== controller) return;
            const loaded = {
                featureSnapshotJson: getValue(payload, 'FeatureSnapshotJson') || '{}',
                modelDecisionReasons: getValue(payload, 'ModelDecisionReasonsJson') || '[]',
                modelExplanation: getValue(payload, 'ModelExplanation')
            };
            evidenceCache.set(id, loaded);
            renderDetail({ ...row, ...loaded });
        } catch (error) {
            if (error.name !== 'AbortError' && evidenceController === controller)
                renderDetail(row, error.message || 'No se pudo cargar la evidencia. Vuelve a abrir el detalle para reintentar.');
        }
    });

    const settlementForm = document.getElementById('BotResearchSettlementForm');
    const settlementModalElement = document.getElementById('BotResearchSettlementModal');
    const settlementModal = settlementModalElement && window.bootstrap
        ? bootstrap.Modal.getOrCreateInstance(settlementModalElement) : null;
    const actualInput = document.getElementById('BotResearchActualValue');
    const reasonInput = document.getElementById('BotResearchSettlementReason');
    const settlementError = document.getElementById('BotResearchSettlementError');
    const settlementPreview = document.getElementById('BotResearchSettlementPreview');
    const settlementSave = document.getElementById('BotResearchSettlementSave');
    let settlementRow = null;
    let settlementRequestId = null;
    let settlementPayloadSignature = null;
    let settlementSaving = false;

    const previewSettlement = () => {
        if (!settlementRow || !actualInput || !settlementPreview) return;
        const actual = number(actualInput.value);
        if (actual === null || !Number.isInteger(actual) || actual < 0 || actual > 1000) {
            settlementPreview.textContent = 'Ingresa el resultado final para calcular la liquidación.';
            return;
        }
        const line = Number(getValue(settlementRow, 'LineValue'));
        const side = getValue(settlementRow, 'SelectedSide');
        const fractional = Math.round(line * 100) % 100;
        const quarter = fractional === 25 || fractional === 75;
        const result = value => actual === value ? 0 : (side === 'Over' ? actual > value : actual < value) ? 1 : -1;
        const factor = quarter ? (result(line - .25) + result(line + .25)) / 2 : result(line);
        const status = factor === 1 ? 'Win' : factor === .5 ? 'HalfWin' : factor === 0 ? 'Push' : factor === -.5 ? 'HalfLoss' : 'Loss';
        const profit = factor > 0 ? factor * (Number(getValue(settlementRow, 'SelectedOdds')) - 1) : factor;
        settlementPreview.textContent = `${humanize(status)} · P/L ${formatNumber(profit)}u por cada 1u`;
    };

    tableBody.addEventListener('click', event => {
        const button = event.target.closest('[data-research-settle]');
        if (!button || !canSettle || !settlementForm || settlementSaving) return;
        const row = rowsById.get(button.dataset.researchSettle);
        if (!row) return;
        settlementRow = row;
        settlementRequestId = crypto.randomUUID();
        settlementPayloadSignature = null;
        document.getElementById('BotResearchSettlementMatch').textContent = `${getValue(row, 'HomeTeam')} vs ${getValue(row, 'AwayTeam')}`;
        document.getElementById('BotResearchSettlementSignal').textContent = `${getValue(row, 'SelectedSide')} ${formatNumber(getValue(row, 'LineValue'))} @ ${formatNumber(getValue(row, 'SelectedOdds'))}`;
        document.getElementById('BotResearchSettlementLabel').textContent = `Resultado real: ${marketLabel(getValue(row, 'MarketType'))}`;
        actualInput.value = getValue(row, 'ActualValue') ?? '';
        reasonInput.value = getValue(row, 'ManualSettlementReason') ?? '';
        settlementError.classList.add('d-none');
        previewSettlement();
        settlementModal?.show();
    });
    actualInput?.addEventListener('input', previewSettlement);
    settlementForm?.addEventListener('submit', async event => {
        event.preventDefault();
        if (settlementSaving || !settlementRow || !settlementForm.reportValidity()) return;
        const actual = number(actualInput.value);
        if (!Number.isInteger(actual) || actual < 0 || actual > 1000 || !reasonInput.value.trim()) return;
        const signature = JSON.stringify([actual, reasonInput.value.trim()]);
        if (settlementPayloadSignature !== null && signature !== settlementPayloadSignature)
            settlementRequestId = crypto.randomUUID();
        settlementPayloadSignature = signature;
        settlementSaving = true;
        actualInput.disabled = true;
        reasonInput.disabled = true;
        settlementSave.disabled = true;
        settlementSave.textContent = 'Guardando…';
        settlementError.classList.add('d-none');
        try {
            const id = isPublishedOnly(settlementRow)
                ? -Number(getValue(settlementRow, 'PublishedSelectionId')) : getValue(settlementRow, 'EvaluationId');
            const response = await fetch(`${settlementEndpoint}?id=${encodeURIComponent(id)}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json',
                    RequestVerificationToken: filtersForm.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '' },
                body: JSON.stringify({ actualValue: actual, reason: reasonInput.value.trim(), requestId: settlementRequestId })
            });
            if (!response.ok) throw new Error(await readError(response));
            settlementModal?.hide();
            await load(true);
            document.dispatchEvent(new CustomEvent('bot-pick-manually-settled'));
        } catch (error) {
            settlementError.textContent = error instanceof Error ? error.message : 'No se pudo guardar. Puedes reintentar.';
            settlementError.classList.remove('d-none');
        } finally {
            settlementSaving = false;
            actualInput.disabled = false;
            reasonInput.disabled = false;
            settlementSave.disabled = false;
            settlementSave.textContent = 'Guardar liquidación';
        }
    });

    sortButtons.forEach(button => button.addEventListener('click', () => {
        const column = button.dataset.researchSort;
        sortDirection = sortBy === column && sortDirection === 'asc' ? 'desc' : 'asc';
        sortBy = column;
        currentPage = 1;
        renderSort();
        void load();
    }));

    const initialQuery = new URLSearchParams(window.location.search);
    const queryValue = name => Array.from(initialQuery).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1];
    const requestedSort = queryValue('sortBy');
    if (sortButtons.some(button => button.dataset.researchSort === requestedSort)) sortBy = requestedSort;
    if (['asc', 'desc'].includes(queryValue('sortDirection'))) sortDirection = queryValue('sortDirection');
    renderSort();
    const requestedBot = queryValue('botKey');
    if (requestedBot) botKeySelect.value = addBotOption(requestedBot)?.value || '';
    for (const [name, input] of [
        ['marketType', marketTypeSelect], ['botKey', botKeySelect],
        ['modelDecision', modelDecisionSelect], ['publicationStatus', publicationStatusSelect],
        ['pageSize', pageSizeSelect]
    ]) {
        const value = queryValue(name);
        if (value !== undefined && Array.from(input.options).some(option => option.value === value)) {
            input.value = value;
        }
    }
    pageSize = Number(pageSizeSelect.value) || 50;
    const requestedPage = Number(queryValue('page'));
    if (Number.isInteger(requestedPage) && requestedPage > 0) currentPage = requestedPage;
    const requestedSurface = queryValue('surface');
    const savedSurface = localStorage.getItem(storageKey);
    const initialSurface = requestedSurface === 'production' ? 'production'
        : ['general', 'research'].includes(requestedSurface) ? 'research'
        : savedSurface === 'production' ? 'production' : 'research';
    selectSurface(initialSurface, false);
})();
